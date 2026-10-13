using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Mono.Cecil;

namespace ValheimUpscalerPatcher
{
    public static class Patcher
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        public static IEnumerable<string> TargetDLLs
        {
            get { return new string[0]; }
        }

        public static void Initialize()
        {
            try
            {
                DeployFiles();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ValheimUpscalerPatcher] Initialize error: " + ex);
            }
        }

        public static void Patch(AssemblyDefinition assembly)
        {
        }

        private static void DeployFiles()
        {
            string gameDir = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(gameDir)) gameDir = Directory.GetCurrentDirectory();

            // 1. Remove conflicting version.dll if present to prevent assertion crashes
            string versionDll = Path.Combine(gameDir, "version.dll");
            if (File.Exists(versionDll))
            {
                try { File.Delete(versionDll); } catch { }
            }

            // 2. Reliably locate BepInEx folder
            string bepDir = null;
            try
            {
                string patcherLocation = typeof(Patcher).Assembly.Location;
                if (!string.IsNullOrEmpty(patcherLocation) && File.Exists(patcherLocation))
                {
                    DirectoryInfo cur = new DirectoryInfo(Path.GetDirectoryName(patcherLocation));
                    while (cur != null)
                    {
                        if (cur.Name.Equals("BepInEx", StringComparison.OrdinalIgnoreCase))
                        {
                            bepDir = cur.FullName;
                            break;
                        }
                        cur = cur.Parent;
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(bepDir) || !Directory.Exists(bepDir))
            {
                bepDir = Path.Combine(gameDir, "BepInEx");
            }

            if (!Directory.Exists(bepDir))
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--doorstop-target-assembly" && i + 1 < args.Length)
                    {
                        string preloaderPath = args[i + 1];
                        DirectoryInfo cur = new DirectoryInfo(Path.GetDirectoryName(preloaderPath));
                        while (cur != null)
                        {
                            if (cur.Name.Equals("BepInEx", StringComparison.OrdinalIgnoreCase))
                            {
                                bepDir = cur.FullName;
                                break;
                            }
                            cur = cur.Parent;
                        }
                        break;
                    }
                }
            }

            // 3. Search for bundled dxgi.dll runtimes folder
            string sourceDir = null;
            if (Directory.Exists(bepDir))
            {
                try
                {
                    string[] found = Directory.GetFiles(bepDir, "OptiScaler.ini", SearchOption.AllDirectories);
                    foreach (string f in found)
                    {
                        string dir = Path.GetDirectoryName(f);
                        if (!dir.Equals(gameDir, StringComparison.OrdinalIgnoreCase))
                        {
                            sourceDir = dir;
                            break;
                        }
                    }
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(sourceDir) && Directory.Exists(sourceDir))
            {
                string[] files = new string[]
                {
                    "dxgi.dll",
                    "version.dll",
                    "OptiScaler.ini",
                    "OptiScaler.dll",
                    "amd_fidelityfx_dx12.dll",
                    "amd_fidelityfx_framegeneration_dx12.dll",
                    "amd_fidelityfx_upscaler_dx12.dll",
                    "libxess.dll",
                    "libxess_fg.dll",
                    "fakenvapi.dll",
                    "fakenvapi.ini",
                    "nvngx_dlss.dll",
                    "nvngx_dlssd.dll",
                    "nvngx_dlssg.dll",
                    "nvngx_dlssnr.dll"
                };

                foreach (string file in files)
                {
                    string src = Path.Combine(sourceDir, file);
                    string dst = Path.Combine(gameDir, file);
                    if (File.Exists(src))
                    {
                        try
                        {
                            bool needsCopy = !File.Exists(dst);
                            if (!needsCopy)
                            {
                                FileInfo fiSrc = new FileInfo(src);
                                FileInfo fiDst = new FileInfo(dst);
                                if (fiSrc.Length != fiDst.Length)
                                {
                                    needsCopy = true;
                                }
                            }

                            if (needsCopy)
                            {
                                File.Copy(src, dst, true);
                                Console.WriteLine("[ValheimUpscalerPatcher] Auto-deployed: " + file);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[ValheimUpscalerPatcher] Deploy error for " + file + ": " + ex.Message);
                        }
                    }
                }

                // Copy sl_plugins if present
                try
                {
                    string slSrc = Path.Combine(sourceDir, "sl_plugins");
                    if (Directory.Exists(slSrc))
                    {
                        string slDst = Path.Combine(gameDir, "sl_plugins");
                        if (!Directory.Exists(slDst)) Directory.CreateDirectory(slDst);
                        foreach (string sf in Directory.GetFiles(slSrc, "*.dll"))
                        {
                            string target = Path.Combine(slDst, Path.GetFileName(sf));
                            if (!File.Exists(target) || new FileInfo(sf).Length != new FileInfo(target).Length)
                            {
                                File.Copy(sf, target, true);
                            }
                        }
                    }
                }
                catch { }
            }

            // 4. Deploy ValheimUpscalerNative.dll into valheim_Data\Plugins\x86_64
            try
            {
                string unityPluginsDir = Path.Combine(gameDir, "valheim_Data", "Plugins", "x86_64");
                string nativeSrc = null;

                if (!string.IsNullOrEmpty(sourceDir))
                {
                    string candidate = Path.Combine(sourceDir, "ValheimUpscalerNative.dll");
                    if (File.Exists(candidate)) nativeSrc = candidate;
                }

                if (string.IsNullOrEmpty(nativeSrc) && Directory.Exists(bepDir))
                {
                    string[] candidates = new string[]
                    {
                        Path.Combine(bepDir, "plugins", "runtimes", "ValheimUpscalerNative.dll"),
                        Path.Combine(bepDir, "plugins", "ValheimUpscalerNative.dll"),
                        Path.Combine(bepDir, "plugins", "native", "ValheimUpscalerNative.dll"),
                        Path.Combine(bepDir, "plugins", "native", "valheim_Data", "Plugins", "x86_64", "ValheimUpscalerNative.dll")
                    };
                    foreach (string c in candidates)
                    {
                        if (File.Exists(c))
                        {
                            nativeSrc = c;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(nativeSrc))
                    {
                        string[] foundNative = Directory.GetFiles(bepDir, "ValheimUpscalerNative.dll", SearchOption.AllDirectories);
                        if (foundNative.Length > 0)
                        {
                            nativeSrc = foundNative[0];
                        }
                    }
                }

                if (!string.IsNullOrEmpty(nativeSrc) && File.Exists(nativeSrc))
                {
                    if (!Directory.Exists(unityPluginsDir))
                    {
                        Directory.CreateDirectory(unityPluginsDir);
                    }
                    string nativeDst = Path.Combine(unityPluginsDir, "ValheimUpscalerNative.dll");
                    bool needsCopy = !File.Exists(nativeDst);
                    if (!needsCopy)
                    {
                        FileInfo fiSrc = new FileInfo(nativeSrc);
                        FileInfo fiDst = new FileInfo(nativeDst);
                        if (fiSrc.Length != fiDst.Length) needsCopy = true;
                    }

                    if (needsCopy)
                    {
                        File.Copy(nativeSrc, nativeDst, true);
                        Console.WriteLine("[ValheimUpscalerPatcher] Auto-deployed ValheimUpscalerNative.dll to: " + nativeDst);
                    }
                }
                else
                {
                    string currentNative = Path.Combine(unityPluginsDir, "ValheimUpscalerNative.dll");
                    if (!File.Exists(currentNative))
                    {
                        Console.WriteLine("[ValheimUpscalerPatcher] Warning: ValheimUpscalerNative.dll not found in BepInEx packages. Native D3D12 upscaler hooks will be disabled until it is placed in valheim_Data\\Plugins\\x86_64\\.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ValheimUpscalerPatcher] Error deploying ValheimUpscalerNative.dll: " + ex.Message);
            }

            // 5. Eagerly load dxgi.dll so DirectX 12 hooks are established before D3D12 device creation
            try
            {
                string dxgiPath = Path.Combine(gameDir, "dxgi.dll");
                if (File.Exists(dxgiPath))
                {
                    IntPtr hMod = LoadLibrary(dxgiPath);
                    Console.WriteLine("[ValheimUpscalerPatcher] LoadLibrary dxgi.dll handle: " + hMod);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ValheimUpscalerPatcher] LoadLibrary error: " + ex.Message);
            }
        }
    }
}
