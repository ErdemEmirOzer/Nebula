using System.Runtime.CompilerServices;

// Test fixtures reach a small number of deliberately internal members -- the ones
// documented as "test-facing" -- so they can be asserted without being promoted to
// public API. Widening them to public instead would say they are part of the
// interface other code may call, which is the opposite of what they are.
//
// Worth knowing: a single-assembly type-check cannot catch a missing entry here,
// because internal is visible within one compilation. Only Unity's real assembly
// split does. UIDesign/typecheck.sh therefore compiles the runtime first and then
// builds the tests against that DLL.
[assembly: InternalsVisibleTo("Nebula.Tests.EditMode")]
[assembly: InternalsVisibleTo("Nebula.Tests.PlayMode")]
