using System.Net;
using System.IO.Compression;

class CppLibrary
{
    public string Name { get; set; } = "";
    public string? IncludePath { get; set; }
    public string? LibPath { get; set; }
    public string[] LibFiles { get; set; } = [];
    public string DownloadUrl { get; set; } = "";
    public string ExtractSubdir { get; set; } = "";
    public bool UseLocalZipFromDownloads { get; set; } = false;
    public string[] CppFiles { get; set; } = [];
    public string[] IncludeHeaders { get; set; } = [];

    public static CppLibrary Glfw(){
        return new CppLibrary
        {
            Name = "glfw",
            DownloadUrl = "https://github.com/glfw/glfw/releases/download/3.3.8/glfw-3.3.8.bin.WIN64.zip",
            ExtractSubdir = "glfw",
            IncludePath = "include",
            LibPath = "lib-vc2022",
            LibFiles = [ "glfw3.lib" ],
            IncludeHeaders = ["<GLFW/glfw3.h>"]
        };
    }

    public static CppLibrary Glad(){
        return new CppLibrary
        {
            Name = "glad",
            UseLocalZipFromDownloads = true,
            ExtractSubdir = "glad", 
            IncludePath = "include",
            CppFiles = ["src/glad.c"],
            IncludeHeaders = ["<glad/glad.h>"]
        };
    }

    public static CppLibrary ImGUIDocking(){
        return new CppLibrary{
            Name = "imgui",
            DownloadUrl = "https://github.com/ocornut/imgui/archive/refs/heads/docking.zip",
            ExtractSubdir = "imgui-docking",
            IncludePath = "",
            CppFiles = 
            [
                "imgui.cpp",
                "imgui_draw.cpp",
                "imgui_widgets.cpp",
                "imgui_tables.cpp",
                "backends/imgui_impl_glfw.cpp",
                "backends/imgui_impl_opengl3.cpp"
            ],
            IncludeHeaders = 
            [
                "<imgui.h>",
                "<backends/imgui_impl_glfw.h>",
                "<backends/imgui_impl_opengl3.h>"
            ]
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
        List<string> includePaths = [];
        foreach(var lib in libs){
            if(lib.IncludePath != null){
                includePaths.Add($"/I ../third_party/{lib.ExtractSubdir}/{lib.IncludePath} ^\n");
            }
        }
        return string.Join(" ", includePaths);
    }

    static string BuildLinkFlags(CppLibrary[] libs)
    {
        string libPaths = "";
        string libFiles = "";
        foreach(var lib in libs){
            if(lib.LibPath != null){
                libPaths += $"/LIBPATH:../third_party/{lib.ExtractSubdir}/{lib.LibPath} ^\n";
            }
            foreach(var f in lib.LibFiles){
                libFiles += f+" ";
            }
        }
        return libPaths + libFiles;
    }

    static string BuildCppFiles(CppLibrary[] libs){
        string cppFiles = "";
        foreach(var lib in libs){
            foreach(var f in lib.CppFiles){
                cppFiles += $"../third_party/{lib.ExtractSubdir}/{f} ^\n";
            }
        }
        return cppFiles;
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
  {includeFlags} ../build/out.cpp ^
  {cppFiles} /Fe:program.exe ^
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