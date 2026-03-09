using System;
using System.Collections.Generic;

namespace StewardessMCPService.Models
{
    // ────────────────────────────────────────────────────────────────────────────
    //  File read models
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Request to read the full contents of a file.</summary>
    public sealed class ReadFileRequest
    {
        /// <summary>Path relative to the repository root.</summary>
        public string Path { get; set; }

        /// <summary>
        /// Maximum bytes to return.  Defaults to the server's MaxFileReadBytes limit.
        /// The response includes a <see cref="ReadFileResponse.Truncated"/> flag when the file is larger.
        /// </summary>
        public long? MaxBytes { get; set; }

        /// <summary>
        /// When true, returns base64-encoded content in <see cref="ReadFileResponse.ContentBase64"/>
        /// instead of the text field (for binary files).
        /// </summary>
        public bool ReturnBase64 { get; set; } = false;
    }

    /// <summary>Response containing file content and metadata.</summary>
    public sealed class ReadFileResponse
    {
        /// <summary>Repository-relative path of the file.</summary>
        public string RelativePath { get; set; }
        /// <summary>File name without path.</summary>
        public string Name { get; set; }
        /// <summary>File extension including the leading dot.</summary>
        public string Extension { get; set; }

        /// <summary>UTF-8 text content; null for binary or when ReturnBase64 is true.</summary>
        public string Content { get; set; }

        /// <summary>Base64-encoded raw bytes; populated when ReturnBase64 is true or file is binary.</summary>
        public string ContentBase64 { get; set; }

        /// <summary>File size in bytes.</summary>
        public long SizeBytes { get; set; }
        /// <summary>Total line count of the file.</summary>
        public int LineCount { get; set; }
        /// <summary>Detected or requested file encoding (e.g. "utf-8").</summary>
        public string Encoding { get; set; }
        /// <summary>Detected line ending style ("LF", "CRLF", "CR", or "Mixed").</summary>
        public string LineEnding { get; set; }
        /// <summary>UTC timestamp of the last write to this file.</summary>
        public DateTimeOffset LastModified { get; set; }
        /// <summary>True when the file contains non-text (binary) data.</summary>
        public bool IsBinary { get; set; }

        /// <summary>True when the file was larger than the read limit and content was cut.</summary>
        public bool Truncated { get; set; }

        /// <summary>Number of bytes actually returned.</summary>
        public long BytesReturned { get; set; }
    }

    // ── read_file_range ──────────────────────────────────────────────────────────

    /// <summary>Request to read a contiguous range of lines from a file.</summary>
    public sealed class ReadFileRangeRequest
    {
        /// <summary>File path relative to repository root.</summary>
        public string Path { get; set; }

        /// <summary>1-based start line (inclusive).</summary>
        public int StartLine { get; set; } = 1;

        /// <summary>1-based end line (inclusive). -1 means end of file.</summary>
        public int EndLine { get; set; } = -1;

        /// <summary>When true, line numbers are prepended to each line in the output.</summary>
        public bool IncludeLineNumbers { get; set; } = true;
    }

    /// <summary>Response with a range of file lines.</summary>
    public sealed class ReadFileRangeResponse
    {
        /// <summary>Repository-relative path of the file.</summary>
        public string RelativePath { get; set; }
        /// <summary>Actual 1-based start line returned.</summary>
        public int StartLine { get; set; }
        /// <summary>Actual 1-based end line returned.</summary>
        public int EndLine { get; set; }
        /// <summary>Total number of lines in the file.</summary>
        public int TotalLines { get; set; }
        /// <summary>Raw text content of the requested range.</summary>
        public string Content { get; set; }
        /// <summary>Structured per-line results with line numbers.</summary>
        public List<FileLine> Lines { get; set; } = new List<FileLine>();
    }

    /// <summary>A single numbered line from a file.</summary>
    public sealed class FileLine
    {
        /// <summary>1-based line number.</summary>
        public int LineNumber { get; set; }

        /// <summary>Text content of the line (without trailing newline).</summary>
        public string Text { get; set; }
    }

    // ── read_multiple_files ──────────────────────────────────────────────────────

    /// <summary>Request to read multiple files in a single call.</summary>
    public sealed class ReadMultipleFilesRequest
    {
        /// <summary>List of relative paths to read.</summary>
        public List<string> Paths { get; set; } = new List<string>();

        /// <summary>Per-file byte limit; null uses the global MaxFileReadBytes setting.</summary>
        public long? MaxBytesPerFile { get; set; }
    }

    /// <summary>Response containing results for each requested file.</summary>
    public sealed class ReadMultipleFilesResponse
    {
        /// <summary>Per-file read results in the same order as the request paths.</summary>
        public List<FileReadResult> Files { get; set; } = new List<FileReadResult>();
    }

    /// <summary>Result of reading a single file within a read_multiple_files call.</summary>
    public sealed class FileReadResult
    {
        /// <summary>Repository-relative path of the file.</summary>
        public string RelativePath { get; set; }
        /// <summary>True when the file was read successfully.</summary>
        public bool Success { get; set; }
        /// <summary>Text content of the file; null on failure or binary.</summary>
        public string Content { get; set; }
        /// <summary>Size of the file in bytes.</summary>
        public long SizeBytes { get; set; }
        /// <summary>Detected encoding (e.g. "utf-8").</summary>
        public string Encoding { get; set; }
        /// <summary>True when the file was truncated at the byte limit.</summary>
        public bool Truncated { get; set; }
        /// <summary>Error description when Success is false.</summary>
        public string ErrorMessage { get; set; }
    }

    // ── get_file_hash ────────────────────────────────────────────────────────────

    /// <summary>Request to compute the cryptographic hash of a file.</summary>
    public sealed class FileHashRequest
    {
        /// <summary>File path relative to repository root.</summary>
        public string Path { get; set; }

        /// <summary>Hash algorithm: "MD5", "SHA1", "SHA256" (default).</summary>
        public string Algorithm { get; set; } = "SHA256";
    }

    /// <summary>Response containing the computed file hash.</summary>
    public sealed class FileHashResponse
    {
        /// <summary>Repository-relative path of the hashed file.</summary>
        public string RelativePath { get; set; }
        /// <summary>Algorithm used (e.g. "SHA256").</summary>
        public string Algorithm { get; set; }
        /// <summary>Hex-encoded hash value.</summary>
        public string Hash { get; set; }
        /// <summary>File size in bytes.</summary>
        public long SizeBytes { get; set; }
    }

    // ── file structure summary ───────────────────────────────────────────────────

    /// <summary>
    /// Best-effort structural summary of a code file parsed via text heuristics.
    /// Not a full AST — uses regex patterns for common C# / VB constructs.
    /// </summary>
    public sealed class FileStructureSummaryRequest
    {
        /// <summary>File path relative to repository root.</summary>
        public string Path { get; set; }
    }

    /// <summary>Structural summary of a code file parsed via text heuristics.</summary>
    public sealed class FileStructureSummaryResponse
    {
        /// <summary>Repository-relative path of the analysed file.</summary>
        public string RelativePath { get; set; }
        /// <summary>Detected programming language (e.g. "C#").</summary>
        public string Language { get; set; }
        /// <summary>Namespaces found in the file.</summary>
        public List<NamespaceInfo> Namespaces { get; set; } = new List<NamespaceInfo>();
        /// <summary>Top-level types declared outside any namespace.</summary>
        public List<TypeInfo> TopLevelTypes { get; set; } = new List<TypeInfo>();
        /// <summary>Using / import directives found at the top of the file.</summary>
        public List<string> UsingDirectives { get; set; } = new List<string>();
    }

    /// <summary>Namespace declaration found in a code file.</summary>
    public sealed class NamespaceInfo
    {
        /// <summary>Fully-qualified namespace name.</summary>
        public string Name { get; set; }
        /// <summary>1-based line number where the namespace is declared.</summary>
        public int LineNumber { get; set; }
        /// <summary>Types declared within this namespace.</summary>
        public List<TypeInfo> Types { get; set; } = new List<TypeInfo>();
    }

    /// <summary>A type (class, interface, struct, enum, or delegate) found in a code file.</summary>
    public sealed class TypeInfo
    {
        /// <summary>"class", "interface", "struct", "enum", "delegate".</summary>
        public string Kind { get; set; }

        /// <summary>Simple (unqualified) type name.</summary>
        public string Name { get; set; }
        /// <summary>Enclosing namespace, or null for top-level types.</summary>
        public string Namespace { get; set; }
        /// <summary>1-based line number of the type declaration.</summary>
        public int LineNumber { get; set; }
        /// <summary>Members declared within this type.</summary>
        public List<MemberInfo> Members { get; set; } = new List<MemberInfo>();
    }

    /// <summary>A member (method, property, field, constructor, or event) inside a type.</summary>
    public sealed class MemberInfo
    {
        /// <summary>"method", "property", "field", "constructor", "event".</summary>
        public string Kind { get; set; }

        /// <summary>Member name.</summary>
        public string Name { get; set; }
        /// <summary>Return type name for methods; property type for properties; null otherwise.</summary>
        public string ReturnType { get; set; }
        /// <summary>1-based line number of the member declaration.</summary>
        public int LineNumber { get; set; }
        /// <summary>True when the member has public accessibility.</summary>
        public bool IsPublic { get; set; }
        /// <summary>True when the member is static.</summary>
        public bool IsStatic { get; set; }
    }
}
