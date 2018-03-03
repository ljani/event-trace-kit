namespace EventManifestFramework.Collections
{
    using System.Collections.Generic;
    using EventManifestFramework.Support;

    public interface IUniqueCollection<in T>
    {
        bool IsUnique(T item);
        bool IsUnique(T item, IDiagnostics diags);
        bool TryAdd(T item);
        bool TryAdd(T item, IDiagnostics diags);
    }

    public interface IUniqueList<T>
        : IList<T>, IReadOnlyList<T>, IUniqueCollection<T>
    {
    }
}
