using System.Threading;
using System.Threading.Tasks;
using StewardessMCPService.Models;

namespace StewardessMCPService.Services
{
    /// <summary>
    /// Controlled execution of build, test, and custom allow-listed shell commands.
    /// No unrestricted shell access is provided.
    /// </summary>
    public interface ICommandService
    {
        /// <summary>Runs the configured build command (e.g. dotnet build or msbuild).</summary>
        Task<CommandResult> RunBuildAsync(RunBuildRequest request, CancellationToken ct = default);

        /// <summary>Runs the configured test command (e.g. dotnet test).</summary>
        Task<CommandResult> RunTestsAsync(RunTestsRequest request, CancellationToken ct = default);

        /// <summary>
        /// Runs an arbitrary command that must appear in the AllowedCommands whitelist.
        /// </summary>
        Task<CommandResult> RunCustomCommandAsync(RunCustomCommandRequest request, CancellationToken ct = default);

        /// <summary>
        /// Returns true when the given command string is permitted by the
        /// AllowedCommands configuration.
        /// </summary>
        bool IsCommandAllowed(string command);
    }
}
