using System.Runtime.CompilerServices;

// The EditMode test assembly asserts on internal seams (e.g. the escalation
// stage mapping) without widening the public API. Under the dotnet test
// project the same sources compile into one assembly and this is inert.
[assembly: InternalsVisibleTo("Game.Sim.Tests")]
