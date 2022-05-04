﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Coocoo3D.UI.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class UIColorAttribute : UIShowAttribute
    {
        public UIColorAttribute(UIShowType type = UIShowType.Global, string name = null) : base(type, name)
        {

        }
    }
}
