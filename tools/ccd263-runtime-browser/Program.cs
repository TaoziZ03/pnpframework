using Ccd263.RuntimeBrowser;

if (args.Length != 2 || !string.Equals(args[0], "emit", StringComparison.Ordinal))
{
    throw new ArgumentException("usage: ccd263-runtime-browser emit <request.json>");
}

var result = BrowserRuntimeEvidenceAdapter.EmitFromFile(Path.GetFullPath(args[1]));
Console.WriteLine(BrowserRuntimeEvidenceAdapter.SerializeIndented(result));
