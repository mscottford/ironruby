/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public License. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Microsoft Public License, please send an email to 
 * ironruby@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Dynamic;
using IronRuby.Runtime;

namespace IronRuby.Builtins {

    [RubyClass("NilClass", Extends = typeof(Null))]
    public static class NilClassOps {
        #region Public Instance Methods

        [RubyMethodAttribute("&")]
        public static bool And(object self, object obj) {
            Debug.Assert(self == null);
            return false;
        }

        [RubyMethodAttribute("^")]
        public static bool Xor(object self, object obj) {
            Debug.Assert(self == null);
            return obj != null;
        }

        [RubyMethodAttribute("^")]
        public static bool Xor(object self, bool obj) {
            Debug.Assert(self == null);
            return obj;
        }

        [RubyMethodAttribute("|")]
        public static bool Or(object self, object obj) {
            Debug.Assert(self == null);
            return obj != null;
        }

        [RubyMethodAttribute("|")]
        public static bool Or(object self, bool obj) {
            Debug.Assert(self == null);
            return obj;
        }

        [RubyMethodAttribute("nil?")]
        public static bool IsNil(object self) {
            Debug.Assert(self == null);
            return true;
        }

        [RubyMethodAttribute("to_a")]
        public static RubyArray/*!*/ ToArray(object self) {
            Debug.Assert(self == null);
            return new RubyArray();
        }

        [RubyMethodAttribute("to_f")]
        public static double ToDouble(object self) {
            Debug.Assert(self == null);
            return 0.0;
        }

        [RubyMethodAttribute("to_i")]
        public static int ToInteger(object self) {
            Debug.Assert(self == null);
            return 0;
        }

        [RubyMethodAttribute("inspect")]
        public static MutableString Inspect(object self) {
            return MutableString.Create("nil");
        }

        [RubyMethodAttribute("to_s")]
        public static MutableString/*!*/ ToString(object self) {
            Debug.Assert(self == null);
            return MutableString.Create(String.Empty);
        }

        [SpecialName]
        public static bool op_Implicit(Null self) {
            Debug.Assert(self == null);
            return false;
        }

        #endregion
    }
}
