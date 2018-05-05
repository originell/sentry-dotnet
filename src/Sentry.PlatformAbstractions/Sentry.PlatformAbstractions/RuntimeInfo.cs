using System;
using System.Reflection;
using System.Text.RegularExpressions;
#if HAS_RUNTIME_INFORMATION
using System.Runtime.InteropServices;
#endif

namespace Sentry.PlatformAbstractions
{
    // https://github.com/dotnet/corefx/issues/17452
    public static class RuntimeInfo
    {
        private static readonly Regex RuntimeParseRegex = new Regex("^(?<name>[^\\d]+)(?<version>[\\d+\\.]+[^\\s]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Gets the current runtime.
        /// </summary>
        /// <returns>A new instance for the current runtime</returns>
        internal static Runtime GetRuntime()
        {
#if HAS_RUNTIME_INFORMATION
            var runtime = GetFromRuntimeInformation();
#else
            var runtime = GetFromMonoRuntime();
#endif

#if HAS_ENVIRONMENT_VERSION
            runtime = runtime ?? GetFromEnvironmentVariable();
#endif

#if NET45PLUS // .NET Framework 4.5 and later
            SetReleaseAndVersionNetFx(runtime);
#elif NETSTANDARD || NETCOREAPP // Possibly .NET Core
            SetNetCoreVersion(runtime);
#endif
            return runtime;
        }

        internal static Runtime Parse(string rawRuntimeDescription, string name = null)
        {
            if (rawRuntimeDescription == null)
            {
                return name == null
                    ? null
                    : new Runtime(name);
            }

            var match = RuntimeParseRegex.Match(rawRuntimeDescription);
            if (match.Success)
            {
                return new Runtime(
                    name ?? match.Groups["name"].Value.Trim(),
                    match.Groups["version"].Value,
                    raw: rawRuntimeDescription);
            }

            return new Runtime(name, raw: rawRuntimeDescription);
        }

#if NET45PLUS // .NET Framework 4.5 and later
        internal static void SetReleaseAndVersionNetFx(Runtime runtime)
        {
            if (runtime?.IsNetFx() == true)
            {
                var latest = FrameworkInfo.GetLatest(Environment.Version.Major);

                runtime.Release = latest.Release;
                runtime.Version = latest.Version?.ToString();
            }
        }
#endif

#if NETSTANDARD || NETCOREAPP // Possibly .NET Core
        // Known issue on Docker: https://github.com/dotnet/BenchmarkDotNet/issues/448#issuecomment-361027977
        internal static void SetNetCoreVersion(Runtime runtime)
        {
            if (runtime?.IsNetCore() == true)
            {
                // https://github.com/dotnet/BenchmarkDotNet/issues/448#issuecomment-308424100
                var assembly = typeof(System.Runtime.GCSettings).GetTypeInfo().Assembly;
                var assemblyPath = assembly.CodeBase.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                var netCoreAppIndex = Array.IndexOf(assemblyPath, "Microsoft.NETCore.App");
                if (netCoreAppIndex > 0
                    && netCoreAppIndex < assemblyPath.Length - 2)
                {
                    runtime.Version = assemblyPath[netCoreAppIndex + 1];
                }
            }
        }
#endif

#if HAS_RUNTIME_INFORMATION
        internal static Runtime GetFromRuntimeInformation()
        {
            // Prefered API: netstandard2.0
            // https://github.com/dotnet/corefx/blob/master/src/System.Runtime.InteropServices.RuntimeInformation/src/System/Runtime/InteropServices/RuntimeInformation/RuntimeInformation.cs
            // https://github.com/mono/mono/blob/90b49aa3aebb594e0409341f9dca63b74f9df52e/mcs/class/corlib/System.Runtime.InteropServices.RuntimeInformation/RuntimeInformation.cs
            // e.g: .NET Framework 4.7.2633.0, .NET Native, WebAssembly
            var frameworkDescription = RuntimeInformation.FrameworkDescription;

            return Parse(frameworkDescription);
        }
#endif

        internal static Runtime GetFromMonoRuntime()
            => Type.GetType("Mono.Runtime", false)
#if HAS_TYPE_INFO
                ?.GetTypeInfo()
#endif
                ?.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static)
                ?.Invoke(null, null) is string monoVersion
                // The implementation of Mono to RuntimeInformation:
                // https://github.com/mono/mono/blob/90b49aa3aebb594e0409341f9dca63b74f9df52e/mcs/class/corlib/System.Runtime.InteropServices.RuntimeInformation/RuntimeInformation.cs#L40
                // Examples:
                // Mono 5.10.1.47 (2017-12/8eb8f7d5e74 Fri Apr 13 20:18:12 EDT 2018)
                // Mono 5.10.1.47 (tarball Tue Apr 17 09:23:16 UTC 2018)
                // Mono 5.10.0 (Visual Studio built mono)
                ? Parse(monoVersion, "Mono")
                : null;


#if HAS_ENVIRONMENT_VERSION
        // This should really only be used on .NET 1.0, 1.1, 2.0, 3.0, 3.5 and 4.0
        internal static Runtime GetFromEnvironmentVariable()
        {
            // Environment.Version: NET Framework 4, 4.5, 4.5.1, 4.5.2 = 4.0.30319.xxxxx
            // .NET Framework 4.6, 4.6.1, 4.6.2, 4.7, 4.7.1 =  4.0.30319.42000
            // Not recommended on NET45+ (which has RuntimeInformation)
            var version = Environment.Version;

            string friendlyVersion = null;
            switch (version.Major)
            {
                case 1:
                    friendlyVersion = "";
                    break;
                //case "4.0.30319.42000":
                //    Debug.Fail("This is .NET Framework 4.6 or later which support RuntimeInformation");
                //    break;
                default:
                    friendlyVersion = version.ToString();
                    break;

            }
            return new Runtime(".NET Framework", friendlyVersion, raw: "Environment.Version=" + version);
        }
#endif

    }
}
