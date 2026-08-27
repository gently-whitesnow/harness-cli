using System.Xml.Linq;

namespace Harness.Checks.DotNet;

internal sealed record DotNetFile(string Path, XElement Root);
