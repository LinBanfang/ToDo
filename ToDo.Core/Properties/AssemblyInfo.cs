using System.Runtime.CompilerServices;

// Expose internal members (e.g. sync tracker internals) to the test project.
[assembly: InternalsVisibleTo("ToDo.Tests")]
