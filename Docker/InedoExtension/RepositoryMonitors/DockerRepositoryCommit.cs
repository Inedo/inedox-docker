using System;
using System.Collections.Generic;
using System.Linq;
using Inedo.ExecutionEngine;
using Inedo.Extensibility.RepositoryMonitors;
using Inedo.Serialization;

namespace Inedo.Extensions.Docker.RepositoryMonitors
{
    [Serializable]
    internal sealed class DockerRepositoryCommit : RepositoryCommit
    {
        [Persistent]
        public string Digest { get; set; }

        public override bool Equals(RepositoryCommit other)
        {
            if (!(other is DockerRepositoryCommit dockerDigest))
                return false;

            return string.Equals(this.Digest, dockerDigest.Digest, StringComparison.OrdinalIgnoreCase);
        }
        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(this.Digest ?? string.Empty);

        public override string GetFriendlyDescription() => this.ToString();

        public override string ToString()
        {
            var digest = this.Digest?.Split(':').LastOrDefault();

            if (digest?.Length > 8)
                return digest.Substring(0, 8);
            else
                return digest ?? string.Empty;
        }

        public override IReadOnlyDictionary<RuntimeVariableName, RuntimeValue> GetRuntimeVariables()
        {
            return new Dictionary<RuntimeVariableName, RuntimeValue>
            {
                [new RuntimeVariableName("ImageDigest", RuntimeValueType.Scalar)] = this.Digest
            };
        }
    }
}
