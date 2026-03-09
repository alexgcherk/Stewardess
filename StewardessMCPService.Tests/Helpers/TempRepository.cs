// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;

namespace StewardessMCPService.Tests.Helpers
{
    /// <summary>
    /// Creates a temporary directory tree that mimics a real repository.
    /// Automatically deleted when disposed.
    /// </summary>
    public sealed class TempRepository : IDisposable
    {
        public string Root { get; }

        public TempRepository()
        {
            Root = Path.Combine(Path.GetTempPath(), "McpTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        // ── Factory helpers ──────────────────────────────────────────────────────

        /// <summary>Creates a file with the given relative path and content.</summary>
        public string CreateFile(string relativePath, string content = "")
        {
            var abs = Abs(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            File.WriteAllText(abs, content ?? "");
            return abs;
        }

        /// <summary>Creates a directory at the given relative path.</summary>
        public string CreateDirectory(string relativePath)
        {
            var abs = Abs(relativePath);
            Directory.CreateDirectory(abs);
            return abs;
        }

        /// <summary>Returns the absolute path for a relative path inside the repo.</summary>
        public string Abs(string relativePath) =>
            Path.GetFullPath(Path.Combine(Root, relativePath.TrimStart('\\', '/')));

        // ── Preset structures ────────────────────────────────────────────────────

        /// <summary>
        /// Builds a minimal C# solution structure for use in tests:
        ///   src/MyLib/MyLib.csproj
        ///   src/MyLib/Class1.cs
        ///   tests/MyLib.Tests/MyLib.Tests.csproj
        ///   tests/MyLib.Tests/Class1Tests.cs
        ///   MySolution.sln
        ///   packages.config (root)
        /// </summary>
        public void CreateSampleCsStructure()
        {
            CreateFile("MySolution.sln",
                "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
                "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"MyLib\", \"src\\MyLib\\MyLib.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\r\n" +
                "EndProject");

            CreateFile(@"src\MyLib\MyLib.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net472</TargetFramework></PropertyGroup></Project>");

            CreateFile(@"src\MyLib\Class1.cs",
                "namespace MyLib\r\n{\r\n    public class Class1\r\n    {\r\n        public string Hello() => \"world\";\r\n    }\r\n}");

            CreateFile(@"src\MyLib\IService.cs",
                "namespace MyLib\r\n{\r\n    public interface IService\r\n    {\r\n        void Execute();\r\n    }\r\n}");

            CreateFile(@"tests\MyLib.Tests\MyLib.Tests.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net472</TargetFramework></PropertyGroup></Project>");

            CreateFile(@"tests\MyLib.Tests\Class1Tests.cs",
                "using Xunit;\r\nnamespace MyLib.Tests\r\n{\r\n    public class Class1Tests\r\n    {\r\n        [Fact]\r\n        public void Hello_Returns_World()\r\n        {\r\n            Assert.Equal(\"world\", new MyLib.Class1().Hello());\r\n        }\r\n    }\r\n}");

            CreateFile("packages.config",
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<packages>\r\n</packages>");

            CreateFile("appsettings.json",
                "{ \"ConnectionStrings\": {} }");
        }

        // ── IDisposable ──────────────────────────────────────────────────────────

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
