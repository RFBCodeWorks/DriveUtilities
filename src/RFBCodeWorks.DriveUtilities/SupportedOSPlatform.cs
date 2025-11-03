using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace RFBCodeWorks.DriveUtilities
{
#if NETFRAMEWORK

    internal sealed class SupportedOSPlatformAttribute(string platform) : Attribute { }
#endif
}
