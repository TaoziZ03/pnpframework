$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$token = az account get-access-token `
    --resource 'https://microsoft.sharepoint.com/' `
    --query accessToken `
    --output tsv
if ([string]::IsNullOrWhiteSpace($token)) {
    throw 'azure_cli_access_token_unavailable'
}

$cases = @(
    [ordered]@{
        caseId = 'ccd35-03'
        webUrl = 'https://microsoft.sharepoint.com/teams/mswikis-gbi/Getfit'
        filePath = '/teams/mswikis-gbi/Getfit/Getfit Program/Home.aspx'
    },
    [ordered]@{
        caseId = 'ccd35-06'
        webUrl = 'https://microsoft.sharepoint.com/teams/campusipkits/industryipkitcybersecurity'
        filePath = '/teams/campusipkits/industryipkitcybersecurity/Pages/Settings.aspx'
    },
    [ordered]@{
        caseId = 'ccd35-08'
        webUrl = 'https://microsoft.sharepoint.com/teams/office_rdx/rm'
        filePath = '/teams/office_rdx/rm/SitePages/Mac Office Release Wiki.aspx'
    }
)

$handler = [Net.Http.HttpClientHandler]::new()
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(20)
$client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
$client.DefaultRequestHeaders.Accept.ParseAdd('application/json;odata=nometadata')

try {
    $results = foreach ($case in $cases) {
        $escaped = $case.filePath.Replace("'", "''").Replace(' ', '%20')
        $url = $case.webUrl + "/_api/web/GetFileByServerRelativePath(decodedUrl='" + $escaped + "')?`$select=UniqueId,UIVersionLabel,TimeLastModified,Length,ServerRelativeUrl"
        try {
            $response = $client.GetAsync($url).GetAwaiter().GetResult()
            $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $body = $null
            if ($response.IsSuccessStatusCode) {
                $body = $text | ConvertFrom-Json
            }
            [ordered]@{
                caseId = $case.caseId
                operation = 'authenticated-source-version-get'
                sourceMutationCount = 0
                status = [int]$response.StatusCode
                requestGuid = if ($response.Headers.Contains('SPRequestGuid')) { [string]::Join(',', $response.Headers.GetValues('SPRequestGuid')) } else { $null }
                etag = if ($response.Headers.ETag) { $response.Headers.ETag.ToString() } else { $null }
                lastModified = if ($response.Content.Headers.LastModified) { $response.Content.Headers.LastModified.ToString('o') } elseif ($body.TimeLastModified) { [string]$body.TimeLastModified } else { $null }
                uniqueId = if ($body) { [string]$body.UniqueId } else { $null }
                versionLabel = if ($body) { [string]$body.UIVersionLabel } else { $null }
                length = if ($body) { [long]$body.Length } else { $null }
                serverRelativeUrl = if ($body) { [string]$body.ServerRelativeUrl } else { $case.filePath }
                error = if ($response.IsSuccessStatusCode) { $null } else { $text.Substring(0, [Math]::Min(600, $text.Length)) }
            }
        }
        catch {
            [ordered]@{
                caseId = $case.caseId
                operation = 'authenticated-source-version-get'
                sourceMutationCount = 0
                status = 0
                error = $_.Exception.GetType().FullName + ': ' + $_.Exception.Message
            }
        }
    }

    [ordered]@{
        schema = 'ccd103.source-version-recheck/v1'
        observedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        provider = 'Azure CLI delegated token'
        tenantId = '72f988bf-86f1-41af-91ab-2d7cd011db47'
        identity = 'titao@microsoft.com'
        audience = 'https://microsoft.sharepoint.com/'
        mode = 'authenticated-read-only'
        sourceMutationCount = 0
        results = @($results)
    } | ConvertTo-Json -Depth 8 -Compress
}
finally {
    $client.Dispose()
    $handler.Dispose()
    $token = $null
}
