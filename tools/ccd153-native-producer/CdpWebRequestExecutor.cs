using Microsoft.SharePoint.Client;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using IOFile = System.IO.File;

sealed class CdpWebRequestExecutorFactory : WebRequestExecutorFactory
{
    public override WebRequestExecutor CreateWebRequestExecutor(ClientRuntimeContext context, string requestUrl) =>
        new CdpWebRequestExecutor(context, requestUrl);
}

sealed class CdpWebRequestExecutor : WebRequestExecutor
{
    private readonly string requestUrl;
    private readonly HttpWebRequest webRequest;
    private MemoryStream requestStream = new();
    private MemoryStream responseStream = new();
    private WebHeaderCollection responseHeaders = new();
    private HttpStatusCode statusCode;
    private string responseContentType;

    public CdpWebRequestExecutor(ClientRuntimeContext context, string requestUrl)
    {
        this.requestUrl = requestUrl ?? throw new ArgumentNullException(nameof(requestUrl));
        webRequest = System.Net.WebRequest.CreateHttp(requestUrl);
        webRequest.Timeout = context.RequestTimeout;
        webRequest.Method = "POST";
    }

    public override HttpWebRequest WebRequest => webRequest;
    public override string RequestContentType { get; set; }
    public override string RequestMethod { get; set; } = "POST";
    public override bool RequestKeepAlive { get; set; }
    public override WebHeaderCollection RequestHeaders => webRequest.Headers;
    public override HttpStatusCode StatusCode => statusCode;
    public override string ResponseContentType => responseContentType;
    public override WebHeaderCollection ResponseHeaders => responseHeaders;
    public override Stream GetRequestStream() => requestStream;
    public override Stream GetResponseStream() { responseStream.Position = 0; return responseStream; }
    public override void Execute() => ExecuteAsync().GetAwaiter().GetResult();

    public override async Task ExecuteAsync()
    {
        var script = Environment.GetEnvironmentVariable("CCD153_CDP_EXECUTOR_SCRIPT");
        if (string.IsNullOrWhiteSpace(script) || !IOFile.Exists(script))
        {
            throw new InvalidOperationException("cdp_executor_script_unavailable");
        }
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in webRequest.Headers)
        {
            if (string.Equals(name, "Cookie", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            headers[name] = webRequest.Headers[name];
        }
        if (!string.IsNullOrWhiteSpace(RequestContentType)) headers["Content-Type"] = RequestContentType;
        var payload = JsonSerializer.Serialize(new
        {
            url = requestUrl,
            method = RequestMethod ?? "POST",
            headers,
            bodyBase64 = Convert.ToBase64String(requestStream.ToArray())
        });
        var expression = "(async()=>{const p=" + payload + ";const h={...p.headers};const u=new URL(p.url);"
            + "if((p.method||'POST').toUpperCase()!=='GET'&&!h['X-RequestDigest']){const base=u.pathname.includes('/_vti_bin/')?u.pathname.split('/_vti_bin/')[0]:'';"
            + "const c=await fetch(u.origin+base+'/_api/contextinfo',{method:'POST',headers:{Accept:'application/json;odata=nometadata'},credentials:'include',cache:'no-store'});"
            + "const j=await c.json();h['X-RequestDigest']=j.FormDigestValue||j.d?.GetContextWebInformation?.FormDigestValue;}"
            + "h['X-FORMS_BASED_AUTH_ACCEPTED']='f';"
            + "const b=p.bodyBase64?Uint8Array.from(atob(p.bodyBase64),c=>c.charCodeAt(0)):undefined;"
            + "const r=await fetch(p.url,{method:p.method,headers:h,body:b,credentials:'include',cache:'no-store',redirect:'follow'});"
            + "const a=new Uint8Array(await r.arrayBuffer());let s='';for(let i=0;i<a.length;i+=32768)s+=String.fromCharCode(...a.subarray(i,i+32768));"
            + "return {status:r.status,contentType:r.headers.get('content-type'),requestGuid:r.headers.get('sprequestguid'),bodyBase64:btoa(s)};})()";
        var expressionPath = Path.Combine(Path.GetTempPath(), "ccd153-cdp-" + Guid.NewGuid().ToString("N") + ".js");
        await IOFile.WriteAllTextAsync(expressionPath, expression, new UTF8Encoding(false));
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var value in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-ExpressionPath", expressionPath, "-TargetHost", new Uri(requestUrl).Host })
            {
                info.ArgumentList.Add(value);
            }
            using var process = Process.Start(info) ?? throw new InvalidOperationException("cdp_executor_start_failed");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0) throw new InvalidOperationException("cdp_executor_failed:" + process.ExitCode + ":" + stderr.Trim());
            var result = JsonSerializer.Deserialize<CdpResponse>(stdout.Trim(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("cdp_executor_response_invalid");
            statusCode = (HttpStatusCode)result.Status;
            responseContentType = result.ContentType;
            responseHeaders = new WebHeaderCollection();
            if (!string.IsNullOrWhiteSpace(result.ContentType)) responseHeaders["Content-Type"] = result.ContentType;
            if (!string.IsNullOrWhiteSpace(result.RequestGuid)) responseHeaders["SPRequestGuid"] = result.RequestGuid;
            responseStream.Dispose();
            responseStream = new MemoryStream(string.IsNullOrWhiteSpace(result.BodyBase64) ? Array.Empty<byte>() : Convert.FromBase64String(result.BodyBase64));
            if (result.Status >= 400)
            {
                var bodyText = Encoding.UTF8.GetString(responseStream.ToArray());
                throw new InvalidOperationException("cdp_http_error:" + result.Status + ":" + bodyText.Substring(0, Math.Min(bodyText.Length, 800)));
            }
        }
        finally
        {
            try { IOFile.Delete(expressionPath); } catch { }
        }
    }

    public override void Dispose()
    {
        requestStream.Dispose();
        responseStream.Dispose();
        base.Dispose();
    }

    private sealed class CdpResponse
    {
        public int Status { get; set; }
        public string ContentType { get; set; }
        public string RequestGuid { get; set; }
        public string BodyBase64 { get; set; }
    }
}
