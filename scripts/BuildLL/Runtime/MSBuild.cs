using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using static BuildLL.Runtime;

namespace BuildLL
{
    public enum VSVersion
    {
        Any = 0,
        VS2019 = 1,
        VS2022 = 2,
        VS2026 = 3
    }
    public enum MSBuildPlatform
    {
        x86,
        x64
    }
    public class MSBuild
    {
        static readonly string[] _editions = {
            "Enterprise", "Professional", "Community", "BuildTools"
        };
        static readonly List<string> paths = new List<string>();

        static string VSWherePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio",
                "Installer",
                "vswhere.exe");
        }

        static string VersionRange(VSVersion vs)
        {
            return vs switch
            {
                VSVersion.VS2019 => "[16.0,17.0)",
                VSVersion.VS2022 => "[17.0,18.0)",
                VSVersion.VS2026 => "[18.0,19.0)",
                _ => throw new ArgumentOutOfRangeException(nameof(vs))
            };
        }

        static string MSBuildSearchPattern(MSBuildPlatform platform)
        {
            if (platform == MSBuildPlatform.x64)
                return @"MSBuild\**\Bin\amd64\MSBuild.exe";
            return @"MSBuild\**\Bin\MSBuild.exe";
        }

        static bool TryFindVSWhere(VSVersion vs, MSBuildPlatform platform, out string msbuild)
        {
            msbuild = null;
            var vswhere = VSWherePath();
            paths.Add(vswhere);
            if (!File.Exists(vswhere))
                return false;

            var args = $"-latest -products * -requires Microsoft.Component.MSBuild " +
                       $"-version {VersionRange(vs)} -find {MSBuildSearchPattern(platform)}";
            var psi = new ProcessStartInfo(vswhere, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var process = Process.Start(psi);
            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                return false;

            foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Trim();
                paths.Add(candidate);
                if (File.Exists(candidate))
                {
                    msbuild = candidate;
                    return true;
                }
            }
            return false;
        }

        static string VSPath(string ver, string[] editions, string msbuildVer, MSBuildPlatform platform)
        {
            // VS 2022 can exist in either Program Files or X86
            // Just check all combinations
            string[] checkFolders =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            ];
            foreach (var pg in checkFolders)
            {
                foreach (var e in editions)
                {
                    var bin = Path.Combine(pg, "Microsoft Visual Studio", ver, e, "MSBuild", msbuildVer, "Bin");
                    var candidate = platform == MSBuildPlatform.x64
                        ? Path.Combine(bin, "amd64", "MSBuild.exe")
                        : Path.Combine(bin, "MSBuild.exe");
                    paths.Add(candidate);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            return null;
        }

        static bool TryFind(VSVersion vs, MSBuildPlatform platform, out string msbuild)
        {
            msbuild = null;
            if (TryFindVSWhere(vs, platform, out msbuild))
                return true;
            if (vs == VSVersion.VS2019)
            {
                msbuild = VSPath("2019", _editions, "Current", platform);
                if (msbuild == null) msbuild = VSPath("2019", _editions, "16.0", platform);
            }
            if (vs == VSVersion.VS2022)
            {
                msbuild = VSPath("2022", _editions, "Current", platform);
                if (msbuild == null) msbuild = VSPath("2022", _editions, "17.0", platform);
            }
            if (vs == VSVersion.VS2026)
            {
                msbuild = VSPath("18", _editions, "Current", platform);
                if (msbuild == null) msbuild = VSPath("18", _editions, "18.0", platform);
            }
            return msbuild != null;
        }

        public static VSVersion SelectVersion(VSVersion vs, MSBuildPlatform platform)
        {
            if (vs == VSVersion.Any)
            {
                if (TryFind(VSVersion.VS2026, platform, out _))
                {
                    Console.WriteLine("Detected VS2026");
                    return VSVersion.VS2026;
                }
                if (TryFind(VSVersion.VS2022, platform, out _))
                {
                    Console.WriteLine("Detected VS2022");
                    return VSVersion.VS2022;
                }
                if (TryFind(VSVersion.VS2019, platform, out _))
                {
                    Console.WriteLine("Detected VS2019");
                    return VSVersion.VS2019;
                }
                throw new Exception($"Could not find installed MSBuild for {vs} {platform}");
            }
            if (!TryFind(vs, platform, out _))
            {
                throw new Exception($"Could not find installed MSBuild for {vs} {platform}");
            }
            return vs;
        }

        static Exception MSBuildNotFound(VSVersion vs, MSBuildPlatform platform)
        {
            return new Exception(
                $"Could not find installed MSBuild for {vs} {platform}. " +
                $"Checked vswhere at {VSWherePath()} and searched: {string.Join(", ", paths)}");
        }

        public static VSVersion SelectVersion(VSVersion vs, MSBuildPlatform platform)
        {
            paths.Clear();
            if (vs == VSVersion.Any)
            {
                if (TryFind(VSVersion.VS2026, platform, out _))
                {
                    Console.WriteLine("Detected VS2026");
                    return VSVersion.VS2026;
                }
                if (TryFind(VSVersion.VS2022, platform, out _))
                {
                    Console.WriteLine("Detected VS2022");
                    return VSVersion.VS2022;
                }
                if (TryFind(VSVersion.VS2019, platform, out _))
                {
                    Console.WriteLine("Detected VS2019");
                    return VSVersion.VS2019;
                }
                throw MSBuildNotFound(vs, platform);
            }
            if (!TryFind(vs, platform, out _))
            {
                throw MSBuildNotFound(vs, platform);
            }
            return vs;
        }

        public static void Run(string file, string args, VSVersion vs, MSBuildPlatform platform)
        {
            if (vs == VSVersion.Any)
            {
                throw new InvalidOperationException("Call SelectVersion()");
            }
            if (!TryFind(vs, platform, out var msbuild))
            {
                throw MSBuildNotFound(vs, platform);
            }
            RunCommand(msbuild, $"{Quote(file)} {args}");
        }
    }
}
