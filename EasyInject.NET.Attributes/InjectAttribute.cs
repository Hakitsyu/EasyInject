using System;
using System.Collections.Generic;
using System.Text;

namespace EasyInject.NET.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class InjectAttribute : Attribute
    {
    }
}
