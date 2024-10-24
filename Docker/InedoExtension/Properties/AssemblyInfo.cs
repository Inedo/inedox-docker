using System.Reflection;
using Inedo.Extensibility;

[assembly: AssemblyTitle("Docker")]
[assembly: AssemblyDescription("Provides operations that act on Docker and Docker Compose.")]
[assembly: AssemblyCompany("Inedo, LLC.")]
[assembly: AssemblyCopyright("Copyright © Inedo 2024")]
[assembly: AssemblyProduct("any")]

// Not for ProGet
[assembly: AppliesTo(InedoProduct.BuildMaster | InedoProduct.Otter)]

[assembly: ScriptNamespace("Docker")]

[assembly: AssemblyVersion("3.1.0")]
[assembly: AssemblyFileVersion("3.1.0")]
