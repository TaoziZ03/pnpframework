using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using PnP.Framework.Migration.Pages.Markup;

namespace PnP.Framework.Migration.Pages.Publishing.Layouts
{
    internal static class PublishingPageLayoutMarkupParser
    {
        private static readonly Regex RegisterDirectivePattern = new Regex(
            "<%@\\s*Register\\s+(?<attributes>.*?)%>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ControlPattern = new Regex(
            "<(?<prefix>[A-Za-z_][A-Za-z0-9_]*)\\:(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<attributes>(?:[^>\\\"']|\\\"[^\\\"]*\\\"|'[^']*')*)/?>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex AttributePattern = new Regex(
            "(?<name>[A-Za-z_][A-Za-z0-9_:-]*)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)')",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ResourcePattern = new Regex(
            "(?<attribute>src|href|poster|data)\\s*=\\s*(?:\"(?<value>[^\"]+)\"|'(?<value>[^']+)')",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex CssResourcePattern = new Regex(
            "url\\(\\s*(?:\"(?<value>[^\"]+)\"|'(?<value>[^']+)'|(?<value>[^)\\s]+))\\s*\\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ScriptBlockPattern = new Regex(
            @"<script\b(?<attributes>[^>]*)>(?<code>.*?)</script\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex HtmlCommentPattern = new Regex(
            @"<!--.*?-->",
            RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex AspNetCommentPattern = new Regex(
            @"<%--.*?--%>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex LoadJsFileCallStartPattern = new Regex(
            @"\bloadjsfile\s*\(",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SetAttributeCallStartPattern = new Regex(
            @"\b(?<receiver>[A-Za-z_$][A-Za-z0-9_$]*)\.setAttribute\s*\(",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex LoadJsFileFunctionPattern = new Regex(
            @"\bfunction\s+loadjsfile\s*\(\s*(?<parameter>[A-Za-z_$][A-Za-z0-9_$]*)\s*\)\s*\{",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ScriptElementAssignmentPattern = new Regex(
            @"\b(?<receiver>[A-Za-z_$][A-Za-z0-9_$]*)\s*=\s*document\.createElement\s*\(\s*(?<quote>[""'])script\k<quote>\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SpUrlExpressionPattern = new Regex(
            "^<%\\s*\\$SPUrl:(?<value>.*?)%>$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly HashSet<string> ControlResourceAttributeNames = new HashSet<string>(
            new[]
            {
                "Src", "Href", "Poster", "Data", "ImageUrl", "NodeImageUrl", "RTLNodeImageUrl",
                "ExpandImageUrl", "ExpandImageUrlRtl", "CollapseImageUrl", "CollapseImageUrlRtl",
                "NoExpandImageUrl", "NavigateUrl", "CssFileLocation", "ScriptFile"
            },
            StringComparer.OrdinalIgnoreCase);

        public static PublishingPageLayoutMarkup Parse(string markup)
        {
            if (markup == null)
            {
                throw new ArgumentNullException(nameof(markup));
            }

            var registrations = ParseRegistrations(markup);
            var controls = ParseControls(markup);
            return new PublishingPageLayoutMarkup
            {
                PageDirective = PageDirectiveParser.Parse(markup),
                Registrations = registrations,
                Controls = controls,
                Zones = controls
                    .Where(item => string.Equals(item.ControlName, "WebPartZone", StringComparison.OrdinalIgnoreCase))
                    .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                    .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new PublishingPageLayoutZone { Id = group.Key })
                    .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                RequiredFieldIdentifiers = controls
                    .Select(item => item.FieldName)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Concat(new[] { "Title", "PublishingPageContent" })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                ResourceReferences = ParseResourceReferences(markup)
            };
        }

        private static IList<PublishingPageLayoutRegistration> ParseRegistrations(string markup)
        {
            return RegisterDirectivePattern.Matches(markup).Cast<Match>()
                .Select(match => Attributes(match.Groups["attributes"].Value))
                .Select(attributes => new PublishingPageLayoutRegistration
                {
                    TagPrefix = Value(attributes, "TagPrefix"),
                    Namespace = Value(attributes, "Namespace"),
                    Assembly = Value(attributes, "Assembly")
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.TagPrefix)
                    || !string.IsNullOrWhiteSpace(item.Namespace)
                    || !string.IsNullOrWhiteSpace(item.Assembly))
                .GroupBy(item => $"{item.TagPrefix}|{item.Namespace}|{item.Assembly}", StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.TagPrefix, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Namespace, StringComparer.Ordinal)
                .ToList();
        }

        private static IList<PublishingPageLayoutControl> ParseControls(string markup)
        {
            return ControlPattern.Matches(markup).Cast<Match>()
                .Select(match =>
                {
                    var attributes = Attributes(match.Groups["attributes"].Value);
                    return new PublishingPageLayoutControl
                    {
                        TagPrefix = match.Groups["prefix"].Value,
                        ControlName = match.Groups["name"].Value,
                        Id = Value(attributes, "ID"),
                        FieldName = Value(attributes, "FieldName")
                    };
                })
                .GroupBy(item => $"{item.TagPrefix}|{item.ControlName}|{item.Id}|{item.FieldName}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.TagPrefix, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ControlName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IList<PublishingPageLayoutResourceReference> ParseResourceReferences(string markup)
        {
            var representations = new[] { markup, WebUtility.HtmlDecode(markup) }.Distinct(StringComparer.Ordinal).ToArray();
            var html = representations.SelectMany(value => ResourcePattern.Matches(value).Cast<Match>().Select(match =>
                Reference(match.Groups["attribute"].Value.ToLowerInvariant(), match.Groups["value"].Value)));
            var css = representations.SelectMany(value => CssResourcePattern.Matches(value).Cast<Match>().Select(match =>
                Reference("css-url", match.Groups["value"].Value)));
            var controls = ControlPattern.Matches(markup).Cast<Match>().SelectMany(match =>
                ControlResourceReferences(match.Groups["prefix"].Value, match.Groups["name"].Value, Attributes(match.Groups["attributes"].Value)));
            var dynamicScripts = representations.SelectMany(ParseDynamicScriptReferences);
            return html.Concat(css).Concat(controls).Concat(dynamicScripts)
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .Where(item => !item.Value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                .Where(item => !item.Value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                .Where(item => !item.Value.StartsWith("#", StringComparison.Ordinal))
                .GroupBy(item => $"{item.Attribute}|{item.Value}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.Attribute, StringComparer.Ordinal)
                .ThenBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<PublishingPageLayoutResourceReference> ParseDynamicScriptReferences(string markup)
        {
            var executableMarkup = AspNetCommentPattern.Replace(
                HtmlCommentPattern.Replace(
                    markup ?? string.Empty,
                    match => new string(' ', match.Length)),
                match => new string(' ', match.Length));
            return ScriptBlockPattern.Matches(executableMarkup).Cast<Match>()
                .Where(IsExecutableScriptBlock)
                .SelectMany(match => ParseDynamicScriptCode(match.Groups["code"].Value));
        }

        private static bool IsExecutableScriptBlock(Match match)
        {
            var attributes = Attributes(match?.Groups["attributes"].Value);
            if (string.Equals(Value(attributes, "runat"), "server", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var type = Value(attributes, "type").Split(';')[0].Trim();
            return string.IsNullOrWhiteSpace(type)
                || string.Equals(type, "module", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "text/javascript", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "application/javascript", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "text/ecmascript", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "application/ecmascript", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<PublishingPageLayoutResourceReference> ParseDynamicScriptCode(string code)
        {
            var result = new List<PublishingPageLayoutResourceReference>();
            var loadCalls = FindCalls(code, LoadJsFileCallStartPattern)
                .Where(call => !IsFunctionDeclaration(code, call.Index))
                .ToArray();
            foreach (var call in loadCalls.Where(value => value.Arguments.Count > 0))
            {
                AddDynamicScriptReference(result, "dynamic-script:loadjsfile", call.Arguments[0]);
            }

            var scriptVariables = new HashSet<string>(
                ScriptElementAssignmentPattern.Matches(code).Cast<Match>()
                    .Where(match => IsExecutableJavaScriptPosition(code, match.Index))
                    .Select(match => match.Groups["receiver"].Value),
                StringComparer.Ordinal);
            var sourceAssignments = FindCalls(code, SetAttributeCallStartPattern)
                .Where(call => scriptVariables.Contains(call.Receiver)
                    && call.Arguments.Count >= 2
                    && TryReadJavaScriptStringLiteral(call.Arguments[0], out var attributeName)
                    && string.Equals(attributeName, "src", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var call in sourceAssignments)
            {
                var argument = call.Arguments[1];
                if (TryReadJavaScriptString(argument, out var value))
                {
                    result.Add(Reference("dynamic-script:setAttribute-src", value));
                }
                else if (!IsLoadJsFileParameterAssignment(code, call))
                {
                    result.Add(UnresolvedReference(
                        "dynamic-script:setAttribute-src",
                        argument,
                        "The Page Layout assigns a script src dynamically; the exact required URI cannot be resolved statically."));
                }
            }

            var assignedVariables = new HashSet<string>(
                sourceAssignments.Select(call => call.Receiver),
                StringComparer.Ordinal);
            foreach (var variable in scriptVariables.Where(value => !assignedVariables.Contains(value)))
            {
                result.Add(UnresolvedReference(
                    "dynamic-script:createElement",
                    variable + "=document.createElement('script')",
                    "The Page Layout creates a script element dynamically without a statically resolvable src assignment."));
            }

            return result;
        }

        private static bool IsLoadJsFileParameterAssignment(string code, JavaScriptCall assignment)
        {
            if (assignment == null
                || assignment.Arguments.Count < 2
                || !IsSimpleIdentifier(assignment.Arguments[1]))
            {
                return false;
            }

            foreach (Match function in LoadJsFileFunctionPattern.Matches(code ?? string.Empty))
            {
                if (!IsExecutableJavaScriptPosition(code, function.Index))
                {
                    continue;
                }
                var openBrace = function.Index + function.Value.LastIndexOf('{');
                if (TryFindClosingBrace(code, openBrace, out var closeBrace)
                    && assignment.Index > openBrace
                    && assignment.Index < closeBrace
                    && string.Equals(
                        assignment.Arguments[1].Trim(),
                        function.Groups["parameter"].Value,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryFindClosingBrace(string code, int openBrace, out int closeBrace)
        {
            closeBrace = -1;
            var depth = 1;
            char quote = '\0';
            var escaped = false;
            var lineComment = false;
            var blockComment = false;
            for (var index = openBrace + 1; index < (code?.Length ?? 0); index++)
            {
                var current = code[index];
                var next = index + 1 < code.Length ? code[index + 1] : '\0';
                if (lineComment)
                {
                    if (current == '\r' || current == '\n') lineComment = false;
                    continue;
                }
                if (blockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        blockComment = false;
                        index++;
                    }
                    continue;
                }
                if (quote != '\0')
                {
                    if (escaped) escaped = false;
                    else if (current == '\\') escaped = true;
                    else if (current == quote) quote = '\0';
                    continue;
                }
                if (current == '/' && next == '/')
                {
                    lineComment = true;
                    index++;
                    continue;
                }
                if (current == '/' && next == '*')
                {
                    blockComment = true;
                    index++;
                    continue;
                }
                if (current == '\'' || current == '"' || current == '`')
                {
                    quote = current;
                }
                else if (current == '{')
                {
                    depth++;
                }
                else if (current == '}' && --depth == 0)
                {
                    closeBrace = index;
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<JavaScriptCall> FindCalls(string code, Regex startPattern)
        {
            foreach (Match match in startPattern.Matches(code ?? string.Empty))
            {
                if (!IsExecutableJavaScriptPosition(code, match.Index))
                {
                    continue;
                }

                var openParenthesis = match.Index + match.Value.LastIndexOf('(');
                if (TryReadCallArguments(code, openParenthesis, out var arguments))
                {
                    yield return new JavaScriptCall
                    {
                        Index = match.Index,
                        Receiver = match.Groups["receiver"].Success
                            ? match.Groups["receiver"].Value
                            : null,
                        Arguments = arguments
                    };
                }
            }
        }

        private static bool TryReadCallArguments(
            string code,
            int openParenthesis,
            out IList<string> arguments)
        {
            arguments = Array.Empty<string>();
            if (string.IsNullOrEmpty(code)
                || openParenthesis < 0
                || openParenthesis >= code.Length
                || code[openParenthesis] != '(')
            {
                return false;
            }

            var depth = 1;
            char quote = '\0';
            var escaped = false;
            var lineComment = false;
            var blockComment = false;
            for (var index = openParenthesis + 1; index < code.Length; index++)
            {
                var current = code[index];
                var next = index + 1 < code.Length ? code[index + 1] : '\0';
                if (lineComment)
                {
                    if (current == '\r' || current == '\n') lineComment = false;
                    continue;
                }
                if (blockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        blockComment = false;
                        index++;
                    }
                    continue;
                }
                if (quote != '\0')
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == quote)
                    {
                        quote = '\0';
                    }
                    continue;
                }
                if (current == '/' && next == '/')
                {
                    lineComment = true;
                    index++;
                    continue;
                }
                if (current == '/' && next == '*')
                {
                    blockComment = true;
                    index++;
                    continue;
                }
                if (current == '\'' || current == '"' || current == '`')
                {
                    quote = current;
                    continue;
                }
                if (current == '(')
                {
                    depth++;
                }
                else if (current == ')' && --depth == 0)
                {
                    arguments = SplitTopLevelArguments(
                        code.Substring(openParenthesis + 1, index - openParenthesis - 1));
                    return true;
                }
            }
            return false;
        }

        private static IList<string> SplitTopLevelArguments(string text)
        {
            var result = new List<string>();
            var start = 0;
            var round = 0;
            var square = 0;
            var curly = 0;
            char quote = '\0';
            var escaped = false;
            var lineComment = false;
            var blockComment = false;
            for (var index = 0; index < (text ?? string.Empty).Length; index++)
            {
                var current = text[index];
                var next = index + 1 < text.Length ? text[index + 1] : '\0';
                if (lineComment)
                {
                    if (current == '\r' || current == '\n') lineComment = false;
                    continue;
                }
                if (blockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        blockComment = false;
                        index++;
                    }
                    continue;
                }
                if (quote != '\0')
                {
                    if (escaped) escaped = false;
                    else if (current == '\\') escaped = true;
                    else if (current == quote) quote = '\0';
                    continue;
                }
                if (current == '/' && next == '/')
                {
                    lineComment = true;
                    index++;
                    continue;
                }
                if (current == '/' && next == '*')
                {
                    blockComment = true;
                    index++;
                    continue;
                }
                if (current == '\'' || current == '"' || current == '`')
                {
                    quote = current;
                    continue;
                }
                switch (current)
                {
                    case '(':
                        round++;
                        break;
                    case ')':
                        round--;
                        break;
                    case '[':
                        square++;
                        break;
                    case ']':
                        square--;
                        break;
                    case '{':
                        curly++;
                        break;
                    case '}':
                        curly--;
                        break;
                    case ',' when round == 0 && square == 0 && curly == 0:
                        result.Add(text.Substring(start, index - start).Trim());
                        start = index + 1;
                        break;
                }
            }
            var final = (text ?? string.Empty).Substring(start).Trim();
            if (final.Length > 0 || result.Count > 0)
            {
                result.Add(final);
            }
            return result;
        }

        private static bool IsExecutableJavaScriptPosition(string code, int position)
        {
            char quote = '\0';
            var escaped = false;
            var lineComment = false;
            var blockComment = false;
            for (var index = 0; index < position && index < (code?.Length ?? 0); index++)
            {
                var current = code[index];
                var next = index + 1 < code.Length ? code[index + 1] : '\0';
                if (lineComment)
                {
                    if (current == '\r' || current == '\n') lineComment = false;
                    continue;
                }
                if (blockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        blockComment = false;
                        index++;
                    }
                    continue;
                }
                if (quote != '\0')
                {
                    if (escaped) escaped = false;
                    else if (current == '\\') escaped = true;
                    else if (current == quote) quote = '\0';
                    continue;
                }
                if (current == '/' && next == '/')
                {
                    lineComment = true;
                    index++;
                    continue;
                }
                if (current == '/' && next == '*')
                {
                    blockComment = true;
                    index++;
                    continue;
                }
                if (current == '\'' || current == '"' || current == '`')
                {
                    quote = current;
                }
            }
            return quote == '\0' && !lineComment && !blockComment;
        }

        private static bool IsSimpleIdentifier(string value)
        {
            return Regex.IsMatch(
                value ?? string.Empty,
                @"^\s*[A-Za-z_$][A-Za-z0-9_$]*\s*$",
                RegexOptions.CultureInvariant);
        }

        private static void AddDynamicScriptReference(
            ICollection<PublishingPageLayoutResourceReference> result,
            string attribute,
            string argument)
        {
            if (TryReadJavaScriptString(argument, out var value))
            {
                result.Add(Reference(attribute, value));
                return;
            }

            result.Add(UnresolvedReference(
                attribute,
                argument,
                "The Page Layout invokes loadjsfile with a dynamic expression; the exact required script URI cannot be resolved statically."));
        }

        private static bool TryReadJavaScriptString(string expression, out string value)
        {
            return TryReadJavaScriptStringLiteral(expression, out value)
                && LooksLikeResourceReference(value);
        }

        private static bool TryReadJavaScriptStringLiteral(string expression, out string value)
        {
            value = null;
            var trimmed = (expression ?? string.Empty).Trim();
            if (trimmed.Length < 2
                || trimmed[0] != trimmed[trimmed.Length - 1]
                || trimmed[0] != '\'' && trimmed[0] != '"')
            {
                return false;
            }

            var quote = trimmed[0];
            var inner = trimmed.Substring(1, trimmed.Length - 2);
            for (var index = 0; index < inner.Length; index++)
            {
                if (inner[index] == quote && (index == 0 || inner[index - 1] != '\\'))
                {
                    return false;
                }
            }

            try
            {
                value = Regex.Unescape(inner).Replace("\\/", "/");
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool IsFunctionDeclaration(string markup, int callIndex)
        {
            var start = Math.Max(0, callIndex - 32);
            var prefix = markup.Substring(start, callIndex - start);
            return Regex.IsMatch(
                prefix,
                @"\bfunction\s+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static IEnumerable<PublishingPageLayoutResourceReference> ControlResourceReferences(
            string prefix,
            string controlName,
            IDictionary<string, string> attributes)
        {
            foreach (var attribute in attributes)
            {
                var isRegistrationResource =
                    (string.Equals(controlName, "CssRegistration", StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(attribute.Key, "Name", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(attribute.Key, "After", StringComparison.OrdinalIgnoreCase)))
                    || (string.Equals(controlName, "ScriptLink", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(attribute.Key, "Name", StringComparison.OrdinalIgnoreCase));
                if (!isRegistrationResource && !ControlResourceAttributeNames.Contains(attribute.Key))
                {
                    continue;
                }

                var value = NormalizeResourceReference(attribute.Value);
                if (LooksLikeResourceReference(value))
                {
                    yield return Reference($"control:{prefix}:{controlName}:{attribute.Key}", value);
                }
            }
        }

        private static PublishingPageLayoutResourceReference Reference(string attribute, string value)
        {
            return new PublishingPageLayoutResourceReference
            {
                Attribute = attribute,
                Value = NormalizeResourceReference(value)
            };
        }

        private static PublishingPageLayoutResourceReference UnresolvedReference(
            string attribute,
            string value,
            string diagnostic)
        {
            return new PublishingPageLayoutResourceReference
            {
                Attribute = attribute,
                Value = WebUtility.HtmlDecode(value ?? string.Empty).Trim(),
                IsUnresolvedDynamic = true,
                Diagnostic = diagnostic
            };
        }

        private static string NormalizeResourceReference(string value)
        {
            var decoded = WebUtility.HtmlDecode(value ?? string.Empty).Trim();
            var match = SpUrlExpressionPattern.Match(decoded);
            return match.Success ? match.Groups["value"].Value.Trim() : decoded;
        }

        private static bool LooksLikeResourceReference(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && (value.StartsWith("/", StringComparison.Ordinal)
                    || value.StartsWith("~/", StringComparison.Ordinal)
                    || value.StartsWith("~site/", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("~sitecollection/", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private static Dictionary<string, string> Attributes(string text)
        {
            return AttributePattern.Matches(text ?? string.Empty).Cast<Match>()
                .GroupBy(match => match.Groups["name"].Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => WebUtility.HtmlDecode(group.Last().Groups["value"].Value),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static string Value(IDictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : string.Empty;
        }

        private sealed class JavaScriptCall
        {
            public int Index { get; set; }

            public string Receiver { get; set; }

            public IList<string> Arguments { get; set; } = new List<string>();
        }
    }
}
