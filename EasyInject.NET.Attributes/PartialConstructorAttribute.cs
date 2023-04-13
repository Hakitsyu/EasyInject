using System;
using System.Collections.Generic;
using System.Text;

namespace EasyInject.NET.Attributes
{
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false)]
    public class PartialConstructorAttribute : Attribute
    {
    }
}
