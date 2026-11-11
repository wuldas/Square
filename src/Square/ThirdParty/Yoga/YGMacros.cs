// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.

using System;

namespace Square.Rendering.Yoga
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false)]
    internal sealed class YogaDeprecatedAttribute : Attribute
    {
        public string Message { get; }

        public YogaDeprecatedAttribute(string message)
        {
            Message = message;
        }
    }
}

