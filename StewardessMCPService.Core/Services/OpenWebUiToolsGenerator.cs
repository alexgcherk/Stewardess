// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace StewardessMCPService.Services;

/// <summary>
///     Generates an Open WebUI Python <c>Tools</c> class from the live OpenAPI document
///     served by this service.  The resulting <c>.py</c> file can be pasted directly into
///     the Open WebUI "Tools" editor or committed to the <c>OpenWebUI/</c> directory.
/// </summary>
public sealed class OpenWebUiToolsGenerator
{
    private readonly ISwaggerProvider _swagger;

    public OpenWebUiToolsGenerator(ISwaggerProvider swagger)
    {
        _swagger = swagger;
    }

    // ── Public entry point ───────────────────────────────────────────────────

    /// <summary>Generates the Python source and returns it as a UTF-8 string.</summary>
    public string Generate(string documentName = "v1")
    {
        var doc = _swagger.GetSwagger(documentName);

        var sb = new StringBuilder();
        sb.Append(StaticHeader(doc));

        // Collect all (tag, method-code) pairs so we can emit section comments per tag.
        var methods = new List<(string Tag, string Code)>();
        foreach (var (path, pathItem) in doc.Paths.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var (opType, operation) in pathItem.Operations)
            {
                var httpMethod = opType.ToString().ToUpperInvariant();
                var tag = operation.Tags?.FirstOrDefault()?.Name ?? "Other";
                var code = GenerateMethod(httpMethod, path, operation, doc);
                if (!string.IsNullOrEmpty(code))
                    methods.Add((tag, code));
            }
        }

        string? lastTag = null;
        foreach (var (tag, code) in methods)
        {
            if (tag != lastTag)
            {
                lastTag = tag;
                sb.AppendLine($"    # {'='.ToString().PadRight(60, '=')}");
                sb.AppendLine($"    # {tag} endpoints");
                sb.AppendLine($"    # {'='.ToString().PadRight(60, '=')}");
                sb.AppendLine();
            }
            sb.Append(code);
        }

        return sb.ToString();
    }

    // ── Per-operation code generation ────────────────────────────────────────

    private string GenerateMethod(string httpMethod, string pathStr, OpenApiOperation op, OpenApiDocument doc)
    {
        if (string.IsNullOrWhiteSpace(op.OperationId))
            return string.Empty;

        var methodName = OperationIdToMethodName(op.OperationId);
        var summary = (op.Summary ?? op.Description ?? $"{httpMethod} {pathStr}").Trim();

        var pathParams = (op.Parameters ?? [])
            .Where(p => p.In == ParameterLocation.Path)
            .ToList();

        var queryParams = (op.Parameters ?? [])
            .Where(p => p.In == ParameterLocation.Query)
            .ToList();

        List<PropInfo> bodyProps = [];
        if (op.RequestBody != null)
        {
            var schema = ResolveBodySchema(op.RequestBody, doc);
            if (schema != null)
                bodyProps = ExtractProperties(schema, doc);
        }

        bool hasParams = pathParams.Count > 0 || queryParams.Count > 0 || bodyProps.Count > 0;

        var sb = new StringBuilder();

        // ── Signature ────────────────────────────────────────────────────────
        if (hasParams)
        {
            sb.AppendLine($"    def {methodName}(");
            sb.AppendLine("        self,");

            foreach (var p in pathParams)
            {
                var pyName = ToSnakeCase(p.Name);
                var desc = EscapeString(p.Description ?? p.Name);
                sb.AppendLine($"        {pyName}: str = Field(..., description=\"{desc}\"),");
            }

            foreach (var p in queryParams)
            {
                var pyName = ToSnakeCase(p.Name);
                var desc = EscapeString(p.Description ?? p.Name);
                var (pyType, pyDefault) = QueryParamTypeDef(p, doc);
                if (p.Required)
                    sb.AppendLine($"        {pyName}: {pyType} = Field(..., description=\"{desc}\"),");
                else
                    sb.AppendLine($"        {pyName}: {pyType} = Field({pyDefault}, description=\"{desc}\"),");
            }

            foreach (var prop in bodyProps)
            {
                var desc = EscapeString(prop.Description);
                sb.AppendLine($"        {prop.PyName}: {prop.PyType} = Field({prop.PyDefault}, description=\"{desc}\"),");
            }

            sb.AppendLine("    ) -> str:");
        }
        else
        {
            sb.AppendLine($"    def {methodName}(self) -> str:");
        }

        // ── Docstring ────────────────────────────────────────────────────────
        var docLine = EscapeDocstring(summary);
        sb.AppendLine("        \"\"\"");
        sb.AppendLine($"        {docLine}");
        sb.AppendLine("        \"\"\"");

        // ── Body ─────────────────────────────────────────────────────────────

        // JSON parse lines for complex parameters
        var jsonProps = bodyProps.Where(p => p.IsJson).ToList();
        foreach (var prop in jsonProps)
        {
            var varName = StripJsonSuffix(prop.PyName);
            sb.AppendLine($"        {varName} = json.loads({prop.PyName}) if {prop.PyName} else None");
        }

        // Path with embedded path-params (f-string if needed)
        var interpolatedPath = pathStr;
        foreach (var p in pathParams)
            interpolatedPath = interpolatedPath.Replace("{" + p.Name + "}", "{" + ToSnakeCase(p.Name) + "}");
        var pathExpr = pathParams.Count > 0 ? $"f\"{interpolatedPath}\"" : $"\"{interpolatedPath}\"";

        // Build return statement
        var requestArgs = new List<string> { $"\"{httpMethod}\"", pathExpr };

        if (queryParams.Count > 0)
        {
            var pairs = queryParams
                .Select(p => $"\"{p.Name}\": {ToSnakeCase(p.Name)}")
                .ToList();
            requestArgs.Add($"params={{{string.Join(", ", pairs)}}}");
        }

        if (bodyProps.Count > 0)
        {
            // Emit a named `payload` variable for readability
            var lines = new List<string>();
            foreach (var prop in bodyProps)
            {
                var valExpr = prop.IsJson ? StripJsonSuffix(prop.PyName) : prop.PyName;
                lines.Add($"            \"{prop.OrigName}\": {valExpr},");
            }
            sb.AppendLine("        payload = {");
            foreach (var l in lines) sb.AppendLine(l);
            sb.AppendLine("        }");
            requestArgs.Add("payload=payload");
        }

        sb.AppendLine($"        return self._request({string.Join(", ", requestArgs)})");
        sb.AppendLine();

        return sb.ToString();
    }

    // ── Schema helpers ────────────────────────────────────────────────────────

    private static OpenApiSchema? ResolveBodySchema(OpenApiRequestBody requestBody, OpenApiDocument doc)
    {
        if (!requestBody.Content.TryGetValue("application/json", out var media) &&
            !requestBody.Content.TryGetValue("application/json-patch+json", out media))
        {
            media = requestBody.Content.Values.FirstOrDefault();
        }

        if (media?.Schema == null)
            return null;

        return media.Schema.Reference != null
            ? ResolveRef(media.Schema.Reference, doc)
            : media.Schema;
    }

    private static OpenApiSchema? ResolveRef(OpenApiReference reference, OpenApiDocument doc)
    {
        if (reference?.Id == null) return null;
        return doc.Components.Schemas.TryGetValue(reference.Id, out var s) ? s : null;
    }

    private static List<PropInfo> ExtractProperties(OpenApiSchema schema, OpenApiDocument doc)
    {
        var result = new List<PropInfo>();
        foreach (var (origName, propSchema) in schema.Properties)
        {
            var resolved = propSchema.Reference != null
                ? ResolveRef(propSchema.Reference, doc) ?? propSchema
                : propSchema;

            bool isComplex = IsComplexSchema(resolved, propSchema);
            var pyName = isComplex ? ToSnakeCase(origName) + "_json" : ToSnakeCase(origName);
            var (pyType, pyDefault) = BodyPropTypeDef(resolved, propSchema, isComplex);
            var desc = BuildDescription(origName, resolved, propSchema, isComplex);

            result.Add(new PropInfo(
                PyName: pyName,
                OrigName: origName,
                PyType: pyType,
                PyDefault: pyDefault,
                Description: desc,
                IsJson: isComplex));
        }
        return result;
    }

    private static bool IsComplexSchema(OpenApiSchema resolved, OpenApiSchema raw)
    {
        // A $ref to a named schema (not a primitive), an object, or an array of objects
        if (raw.Reference != null)
        {
            var t = resolved.Type;
            return string.IsNullOrEmpty(t) || t == "object";
        }
        if (resolved.Type == "object") return true;
        if (resolved.Type == "array" && resolved.Items != null)
        {
            var items = resolved.Items;
            return items.Reference != null || items.Type == "object" || string.IsNullOrEmpty(items.Type);
        }
        return false;
    }

    private static (string pyType, string pyDefault) BodyPropTypeDef(
        OpenApiSchema resolved, OpenApiSchema raw, bool isComplex)
    {
        if (isComplex)
            return ("Optional[str]", "default=None");

        return SchemaTypeDef(resolved, raw);
    }

    private static (string pyType, string pyDefault) QueryParamTypeDef(OpenApiParameter p, OpenApiDocument doc)
    {
        var schema = p.Schema;
        if (schema == null) return ("Optional[str]", "default=None");
        var resolved = schema.Reference != null ? ResolveRef(schema.Reference, doc) ?? schema : schema;
        var (pyType, pyDefault) = SchemaTypeDef(resolved, schema);

        // Non-required query params should be Optional so the LLM can omit them.
        if (!p.Required && pyDefault == "...")
        {
            var optType = pyType.StartsWith("Optional[", StringComparison.Ordinal)
                ? pyType
                : $"Optional[{pyType}]";
            return (optType, "default=None");
        }

        return (pyType, pyDefault);
    }

    private static (string pyType, string pyDefault) SchemaTypeDef(OpenApiSchema resolved, OpenApiSchema raw)
    {
        var nullable = raw.Nullable || resolved.Nullable;
        var type = resolved.Type;

        switch (type)
        {
            case "string":
                return nullable
                    ? ("Optional[str]", "default=None")
                    : ("str", "...");  // non-nullable string → required sentinel

            case "integer":
                return nullable
                    ? ("Optional[int]", "default=None")
                    : ("int", "0");

            case "number":
                return nullable
                    ? ("Optional[float]", "default=None")
                    : ("float", "0.0");

            case "boolean":
                return ("bool", "False");

            case "array":
                if (resolved.Items == null) return ("Optional[List[str]]", "default=None");
                var itemType = resolved.Items.Type switch
                {
                    "string" => "str",
                    "integer" => "int",
                    "number" => "float",
                    "boolean" => "bool",
                    _ => "Any"
                };
                return ($"Optional[List[{itemType}]]", "default=None");

            default:
                return ("Optional[str]", "default=None");
        }
    }

    private static string BuildDescription(
        string origName, OpenApiSchema resolved, OpenApiSchema raw, bool isComplex)
    {
        var desc = raw.Description ?? resolved.Description ?? string.Empty;
        if (!string.IsNullOrEmpty(desc)) return desc;

        if (isComplex)
        {
            if (raw.Type == "array" || resolved.Type == "array")
                return $"JSON array for {origName}.";
            return $"JSON object for {origName}.";
        }
        return origName;
    }

    // ── Name helpers ──────────────────────────────────────────────────────────

    private static string OperationIdToMethodName(string operationId)
    {
        // "Capabilities_GetCapabilities" → controller="capabilities", action="get_capabilities"
        // If action already contains controller name, use action only; otherwise prefix with controller.
        var idx = operationId.IndexOf('_');
        if (idx < 0) return ToSnakeCase(operationId);

        var controller = ToSnakeCase(operationId[..idx]);
        var action = ToSnakeCase(operationId[(idx + 1)..]);

        return action.Contains(controller, StringComparison.Ordinal) ? action : $"{controller}_{action}";
    }

    internal static string ToSnakeCase(string s)
    {
        // Handle consecutive uppercase acronyms: "HTTPSResponse" → "https_response"
        s = Regex.Replace(s, @"([A-Z]+)([A-Z][a-z])", "$1_$2");
        // Handle camelCase transitions: "camelCase" → "camel_case"
        s = Regex.Replace(s, @"([a-z\d])([A-Z])", "$1_$2");
        return s.Replace('-', '_').ToLowerInvariant();
    }

    private static string StripJsonSuffix(string name) =>
        name.EndsWith("_json", StringComparison.Ordinal) ? name[..^5] : name;

    // ── String helpers ────────────────────────────────────────────────────────

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");

    private static string EscapeDocstring(string s) =>
        s.Replace("\\", "\\\\").Replace("\"\"\"", "\\\"\\\"\\\"").Replace("\r", "").Replace("\n", " ").Trim();

    // ── Static header ─────────────────────────────────────────────────────────

    private static string StaticHeader(OpenApiDocument doc)
    {
        var title = doc.Info?.Title ?? "Stewardess MCP Tools";
        var version = doc.Info?.Version ?? "v1";

        // Build the static Python preamble using a StringBuilder to avoid
        // any conflict between C# raw-string delimiters and Python triple-quote strings.
        var h = new StringBuilder();
        void L(string s = "") { h.AppendLine(s); }

        L("\"\"\"");
        L($"title: {title}");
        L("author: Stewardess");
        L($"version: {version}");
        L("required_open_webui_version: 0.5.0");
        L("license: Apache-2.0");
        L("\"\"\"");
        L();
        L("from __future__ import annotations");
        L();
        L("import json");
        L("from typing import Any, Dict, List, Optional");
        L();
        L("import requests");
        L("from pydantic import BaseModel, Field");
        L();
        L();
        L("class Tools:");
        L("    class Valves(BaseModel):");
        L("        base_url: str = Field(");
        L("            default=\"http://127.0.0.1:55703\",");
        L("            description=\"Base URL of the Stewardess MCP service.\",");
        L("        )");
        L("        api_key: Optional[str] = Field(");
        L("            default=None,");
        L("            description=\"Optional X-API-Key header value.\",");
        L("        )");
        L("        timeout_seconds: int = Field(");
        L("            default=120,");
        L("            description=\"HTTP timeout in seconds.\",");
        L("        )");
        L("        verify_ssl: bool = Field(");
        L("            default=True,");
        L("            description=\"Verify TLS certificates.\",");
        L("        )");
        L();
        L("    def __init__(self) -> None:");
        L("        self.valves = self.Valves()");
        L();
        L("    # ============================================================");
        L("    # Internal helpers");
        L("    # ============================================================");
        L();
        L("    def _trim(self, value: Any, limit: int = 1024) -> str:");
        L("        try:");
        L("            if isinstance(value, (dict, list)):");
        L("                text = json.dumps(value, ensure_ascii=False, default=str)");
        L("            else:");
        L("                text = str(value)");
        L("        except Exception as ex:");
        L("            text = f\"<unserializable: {ex}>\"");
        L();
        L("        if len(text) > limit:");
        L("            return text[:limit] + \"... [truncated]\"");
        L("        return text");
        L();
        L("    def _log(self, message: str) -> None:");
        L("        print(message, flush=True)");
        L();
        L("    def _headers(self) -> Dict[str, str]:");
        L("        headers = {");
        L("            \"Accept\": \"application/json\",");
        L("            \"Content-Type\": \"application/json\",");
        L("        }");
        L("        if self.valves.api_key:");
        L("            headers[\"X-API-Key\"] = self.valves.api_key");
        L("        return headers");
        L();
        L("    def _normalize_base_url(self) -> str:");
        L("        return self.valves.base_url.rstrip(\"/\")");
        L();
        L("    def _request(");
        L("        self,");
        L("        method: str,");
        L("        path: str,");
        L("        *,");
        L("        params: Optional[Dict[str, Any]] = None,");
        L("        payload: Optional[Dict[str, Any]] = None,");
        L("    ) -> str:");
        L("        url = f\"{self._normalize_base_url()}{path}\"");
        L();
        L("        safe_params = {k: v for k, v in (params or {}).items() if v is not None}");
        L("        safe_payload = {k: v for k, v in (payload or {}).items() if v is not None}");
        L();
        L("        self._log(\"=\" * 80)");
        L("        self._log(f\"HTTP REQUEST: {method.upper()} {url}\")");
        L("        if safe_params:");
        L("            self._log(f\"REQUEST PARAMS (1KB): {self._trim(safe_params)}\")");
        L("        if safe_payload:");
        L("            self._log(f\"REQUEST BODY   (1KB): {self._trim(safe_payload)}\")");
        L();
        L("        try:");
        L("            response = requests.request(");
        L("                method=method.upper(),");
        L("                url=url,");
        L("                headers=self._headers(),");
        L("                params=safe_params if safe_params else None,");
        L("                json=safe_payload if safe_payload else None,");
        L("                timeout=self.valves.timeout_seconds,");
        L("                verify=self.valves.verify_ssl,");
        L("            )");
        L();
        L("            content_type = response.headers.get(\"Content-Type\", \"\")");
        L("            response_preview: Any");
        L("            if \"application/json\" in content_type.lower():");
        L("                try:");
        L("                    response_preview = response.json()");
        L("                except Exception:");
        L("                    response_preview = response.text");
        L("            else:");
        L("                response_preview = response.text");
        L();
        L("            self._log(f\"HTTP RESPONSE: {response.status_code} {response.reason}\")");
        L("            self._log(f\"RESPONSE BODY (1KB): {self._trim(response_preview)}\")");
        L();
        L("            response.raise_for_status()");
        L();
        L("            if \"application/json\" in content_type.lower():");
        L("                try:");
        L("                    return json.dumps(response.json(), indent=2, ensure_ascii=False)");
        L("                except Exception:");
        L("                    return response.text");
        L();
        L("            return response.text");
        L();
        L("        except requests.RequestException as ex:");
        L("            self._log(f\"HTTP ERROR: {ex}\")");
        L("            return f\"Request failed: {ex}\"");
        L();

        return h.ToString();
    }

    // ── Data record ───────────────────────────────────────────────────────────

    private record PropInfo(
        string PyName,
        string OrigName,
        string PyType,
        string PyDefault,
        string Description,
        bool IsJson);
}
