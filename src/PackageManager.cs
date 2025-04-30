using System.Net;
using System.IO.Compression;

class CppLibrary
{
    public string Name { get; set; } = "";
    public string IncludePath { get; set; } = "";
    public string LibPath { get; set; } = "";
    public string[] LibFiles { get; set; } = [];
    public string DownloadUrl { get; set; } = "";
    public string ExtractSubdir { get; set; } = "";
    public bool UseLocalZipFromDownloads { get; set; } = false;
    public string[] CppFiles { get; set; } = [];

    public static CppLibrary Glfw(){
        return new CppLibrary
        {
            Name = "glfw",
            DownloadUrl = "https://github.com/glfw/glfw/releases/download/3.3.8/glfw-3.3.8.bin.WIN64.zip",
            ExtractSubdir = "glfw",
            IncludePath = "third_party/glfw/include",
            LibPath = "third_party/glfw/lib-vc2022",
            LibFiles = [ "glfw3.lib" ]
        };
    }

    public static CppLibrary Glad(){
        return new CppLibrary
        {
            Name = "glad",
            UseLocalZipFromDownloads = true,
            ExtractSubdir = "glad", 
            IncludePath = "third_party/glad/include",
            CppFiles = ["third_party/glad/src/glad.c"]
        };
    }
}

static class PackageManager{
    public static void EnsureLibraryInstalled(CppLibrary lib)
{
    string destPath = Path.Combine("third_party", lib.ExtractSubdir);
    if (Directory.Exists(destPath))
    {
        Console.WriteLine($"✅ {lib.Name} already installed.");
        return;
    }

    Directory.CreateDirectory("third_party");

    string zipPath = "";

    if (lib.UseLocalZipFromDownloads)
    {
        Console.WriteLine($"📦 Searching Downloads folder for {lib.Name} ZIP...");
        string downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads"
        );

        zipPath = Directory.GetFiles(downloads, $"{lib.Name}*.zip")
                           .OrderByDescending(File.GetLastWriteTime)
                           .FirstOrDefault()
                 ?? throw new Exception($"❌ Could not find {lib.Name} ZIP in Downloads.");

        Console.WriteLine($"📥 Found {Path.GetFileName(zipPath)} — importing...");
    }
    else
    {
        // Download from web as usual
        zipPath = Path.Combine("third_party", lib.Name + ".zip");
        using var client = new WebClient();
        client.DownloadFile(lib.DownloadUrl, zipPath);
        Console.WriteLine($"⬇️ Downloaded {lib.Name}.");
    }

    // Extract and flatten
    ZipFile.ExtractToDirectory(zipPath, destPath);
    if (!lib.UseLocalZipFromDownloads) File.Delete(zipPath);

    string inner = Directory.GetDirectories(destPath).FirstOrDefault();
    if (inner != null && Path.GetFileName(inner).Contains(lib.Name))
    {
        foreach (var file in Directory.GetFiles(inner, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(inner, file);
            var dest = Path.Combine(destPath, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Move(file, dest, true);
        }
        Directory.Delete(inner, true);
    }

    Console.WriteLine($"✅ {lib.Name} installed to third_party/{lib.ExtractSubdir}/");
}

    static string BuildIncludeFlags(CppLibrary[] libs)
    {
        return string.Join(" ", libs
            .Where(lib => !string.IsNullOrEmpty(lib.IncludePath))
            .Select(lib => $"/I ../{lib.IncludePath} ^\n"));
    }

    static string BuildLinkFlags(CppLibrary[] libs)
    {
        var libPaths = libs.Where(l => !string.IsNullOrEmpty(l.LibPath))
                        .Select(p => p.LibPath);
        var libFiles = libs.SelectMany(l => l.LibFiles);

        var paths = string.Join(" ", libPaths.Select(p => $"/LIBPATH:../{p} ^\n"));
        var files = string.Join(" ", libFiles);

        return $"{paths} {files}";
    }

    static string BuildCppFiles(CppLibrary[] libs){
        return string.Join(" ", libs.SelectMany(l => l.CppFiles).Select(c=>$"../{c}"));
    }

    public static void Build(CppLibrary[] libs){
        foreach(var lib in libs){
            EnsureLibraryInstalled(lib);
        }
        string includeFlags = BuildIncludeFlags(libs);
        string linkFlags = BuildLinkFlags(libs);
        string cppFiles = BuildCppFiles(libs);

        string buildScript = $@"
pushd build

cl /nologo /EHsc /MD ^
  {includeFlags} ..\build\out.cpp {cppFiles} ^
  /Fe:program.exe ^
  /link ^
  {linkFlags} user32.lib gdi32.lib shell32.lib opengl32.lib

if exist program.exe (
  echo.
  echo ▶️ Running program.exe...
  program.exe
)

popd
        ";

        string scriptPath = Path.Combine("build", "run_msvc.bat");
        Directory.CreateDirectory("build");
        File.WriteAllText(scriptPath, buildScript);
        Console.WriteLine("✅ Generated build\\run_msvc.bat");
    }
}