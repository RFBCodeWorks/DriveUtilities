using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace RFBCodeWorks.DriveUtilities
{
#if NETFRAMEWORK

#pragma warning disable CS9113 // Parameter is unread.
    internal sealed class SupportedOSPlatformAttribute(string platform) : Attribute { }
#pragma warning restore CS9113 // Parameter is unread.
#endif
}
