// MetadataReferences for the BCL DLLs on the test runner's Trusted Platform
// Assemblies list. Built once per test process — `MetadataReference.CreateFromFile`
// opens and reads each PE header, and the TPA list is ~150 entries, so paying
// that cost per test compilation was a meaningful chunk of the suite's runtime.
// MetadataReference instances are intentionally shareable across compilations.
static class TrustedPlatformReferences
{
    public static ImmutableArray<MetadataReference> All { get; } =
        [..((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(_ => MetadataReference.CreateFromFile(_))];
}
