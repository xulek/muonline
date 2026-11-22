using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using MUnique.OpenMU.Network;

namespace Client.Main.Networking
{
    /// <summary>
    /// Works around a packaging mismatch of the OpenMU network packages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The NuGet package <c>MUnique.OpenMU.Network.Packets</c> 0.11.0 declares a dependency on
    /// <c>MUnique.OpenMU.Network</c> 0.10.0, but its assembly was compiled against
    /// <c>MUnique.OpenMU.Network, Version=0.10.1.0</c>, which was never published. The runtime refuses
    /// to bind that reference to the available assembly version 0.10.0.0, so every packet extension
    /// method which touches <see cref="IConnection"/> (e.g. <c>SendServerListRequestAsync</c>) throws
    /// a <see cref="System.IO.FileNotFoundException"/> when it's compiled. Because most of these calls
    /// are fire-and-forget tasks, the client silently stopped at the server list.
    /// </para>
    /// <para>
    /// The source of the network library didn't change between both versions, so the reference is
    /// bound to the already available assembly, as long as it has the same major and minor version.
    /// This class can be removed when the packages are published with matching versions.
    /// </para>
    /// </remarks>
    internal static class OpenMuNetworkAssemblyResolver
    {
        private static readonly Assembly NetworkAssembly = typeof(IConnection).Assembly;

        [ModuleInitializer]
        [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "The resolver has to be registered before any packet extension method is compiled, regardless of the platform head which starts the client.")]
        internal static void Register()
        {
            AssemblyLoadContext.Default.Resolving += ResolveNetworkAssembly;
        }

        private static Assembly ResolveNetworkAssembly(AssemblyLoadContext context, AssemblyName requestedName)
        {
            var availableName = NetworkAssembly.GetName();
            if (!string.Equals(requestedName.Name, availableName.Name, StringComparison.OrdinalIgnoreCase)
                || requestedName.Version is not { } requestedVersion
                || availableName.Version is not { } availableVersion
                || requestedVersion.Major != availableVersion.Major
                || requestedVersion.Minor != availableVersion.Minor)
            {
                return null;
            }

            return NetworkAssembly;
        }
    }
}
