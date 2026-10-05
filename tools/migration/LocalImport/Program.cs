using System.Text.Json;
namespace Qontract.LocalMigration;

internal static class LocalImportEntryPoint
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 3 || args[0] != "--local-only")
        {
            Console.Error.WriteLine("Usage: LocalImport --local-only PRIVATE_PLAN.json NEW_LOCAL_TARGET_DIRECTORY");
            return 1;
        }
        try
        {
            Console.WriteLine(JsonSerializer.Serialize(await LocalFixtureImporter.ImportAsync(args[1], args[2])));
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Local import refused or failed. Source unchanged; retain local evidence for diagnosis.");
            return 1;
        }
    }
}
