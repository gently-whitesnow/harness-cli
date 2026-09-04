using System.Xml.Linq;

namespace Harness.Checks;

internal sealed record DotNetFile(string Path, XElement Root);
