#nullable enable
#pragma warning disable CS0618 // Type or member is obsolete

using System.Runtime.CompilerServices;
using Inedo.Extensibility.Credentials;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensions.Credentials;

namespace Inedo.Extensions.SecureResources;

internal sealed class DockerRepository24
{
    private readonly SecureResource resource;

    private DockerRepository24(SecureResource resource) => this.resource = resource;

    public static DockerRepository24? Create(string name, IResourceResolutionContext context)
    {
        var resource = SecureResource.Create(name, context);
        if (resource is ContainerSource cs)
            return new DockerRepository24(cs) { LegacyRegistryPrefix = cs.RegistryPrefix };
        try
        {
            if (IsDockerRepository(resource))
                return new DockerRepository24(resource);
        }
        catch
        {
        }

        return null;
    }

    public string? Repository { get; set; }
    public string? LegacyRegistryPrefix { get; set; }

    public string? GetRepository(ICredentialResolutionContext credentialResolutionContext)
    {
        try
        {
            return GetRepository(this.resource, credentialResolutionContext);
        }
        catch
        {
            return this.Repository;
        }
    }

    public UsernamePasswordCredentials? GetDockerCredentials(ICredentialResolutionContext credentialResolutionContext)
    {
        try
        {
            return GetDockerCredentials(this.resource, credentialResolutionContext);
        }
        catch
        {
            var creds = this.resource.GetCredentials(credentialResolutionContext);
            if (creds is UsernamePasswordCredentials upc)
                return upc;
            else if (creds is TokenCredentials tc)
                return new UsernamePasswordCredentials { UserName = "api", Password = tc.Token };

            return null;
        }
    }
    
    // replace with "is GenericDockerRepository"
    public bool IsContainerSource(out DockerRepository24 source)
    {
        source = this;
        return this.resource.GetType() == typeof(ContainerSource);
    }


    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsDockerRepository(SecureResource resource) => resource is DockerRepository;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? GetRepository(SecureResource resource, ICredentialResolutionContext credentialResolutionContext) => ((DockerRepository)resource).GetRepository(credentialResolutionContext);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static UsernamePasswordCredentials? GetDockerCredentials(SecureResource resource, ICredentialResolutionContext credentialResolutionContext) => ((DockerRepository)resource).GetDockerCredentials(credentialResolutionContext);
}
#pragma warning restore CS0618 // Type or member is obsolete
