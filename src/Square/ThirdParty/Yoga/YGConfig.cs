// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
//
// Original: yoga/YGConfig.h, yoga/YGConfig.cpp

namespace Square.Rendering.Yoga
{
    /// <summary>
    /// Public C-style API for Yoga configuration.
    /// Delegates to the internal Config class.
    /// </summary>
    internal static class YGConfigAPI
    {
        public static Config YGConfigNew()
        {
            return new Config();
        }

        public static void YGConfigFree(Config config)
        {
            // In C#, GC handles deallocation. No-op.
        }

        public static Config YGConfigGetDefault()
        {
            return Config.Default;
        }

        public static void YGConfigSetUseWebDefaults(Config config, bool enabled)
        {
            config.SetUseWebDefaults(enabled);
        }

        public static bool YGConfigGetUseWebDefaults(Config config)
        {
            return config.UseWebDefaults();
        }

        public static void YGConfigSetPointScaleFactor(Config config, float pixelsInPoint)
        {
            Debug.AssertFatal.AssertWithConfig(
                config,
                pixelsInPoint >= 0.0f,
                "Scale factor should not be less than zero");

            config.SetPointScaleFactor(pixelsInPoint);
        }

        public static float YGConfigGetPointScaleFactor(Config config)
        {
            return config.GetPointScaleFactor();
        }

        public static void YGConfigSetErrata(Config config, YGErrata errata)
        {
            config.SetErrata(errata.ToInternal());
        }

        public static YGErrata YGConfigGetErrata(Config config)
        {
            return config.GetErrata().ToYG();
        }

        public static void YGConfigSetLogger(Config config, YGLogger? logger)
        {
            if (logger != null)
            {
                config.SetLogger(logger);
            }
            else
            {
                config.SetLogger((c, n, l, m) => { });
            }
        }

        public static void YGConfigSetContext(Config config, object? context)
        {
            config.SetContext(context!);
        }

        public static object? YGConfigGetContext(Config config)
        {
            return config.GetContext();
        }

        public static void YGConfigSetExperimentalFeatureEnabled(
            Config config,
            YGExperimentalFeature feature,
            bool enabled)
        {
            config.SetExperimentalFeatureEnabled(feature.ToInternal(), enabled);
        }

        public static bool YGConfigIsExperimentalFeatureEnabled(
            Config config,
            YGExperimentalFeature feature)
        {
            return config.IsExperimentalFeatureEnabled(feature.ToInternal());
        }

        public static void YGConfigSetCloneNodeFunc(Config config, YGCloneNodeFunc? callback)
        {
            if (callback != null)
            {
                config.SetCloneNodeCallback(callback);
            }
            else
            {
                config.SetCloneNodeCallback(null!);
            }
        }
    }
}
