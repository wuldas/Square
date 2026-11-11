using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Square.Rendering.Yoga
{
    internal delegate void YGLogger(Config config, Node node, LogLevel logLevel, string message);
    internal delegate Node? YGCloneNodeFunc(Node node, Node owner, int childIndex);

    internal sealed class ExperimentalFeatureSet : IEquatable<ExperimentalFeatureSet>
    {
        private uint _bits;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(ExperimentalFeature feature, bool enabled)
        {
            uint mask = 1u << (int)feature;
            if (enabled)
                _bits |= mask;
            else
                _bits &= ~mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Test(ExperimentalFeature feature)
        {
            return (_bits & (1u << (int)feature)) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ExperimentalFeatureSet? other)
        {
            if (other is null) return false;
            return _bits == other._bits;
        }

        public override bool Equals(object? obj) => Equals(obj as ExperimentalFeatureSet);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => (int)_bits;

        public static bool operator ==(ExperimentalFeatureSet left, ExperimentalFeatureSet right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left._bits == right._bits;
        }

        public static bool operator !=(ExperimentalFeatureSet left, ExperimentalFeatureSet right) => !(left == right);
    }

    internal class Config
    {
        private YGCloneNodeFunc? _cloneNodeCallback;
        private YGLogger _logger;

        private bool _useWebDefaults = false;
        private uint _version = 0;
        private readonly ExperimentalFeatureSet _experimentalFeatures = new ExperimentalFeatureSet();
        private Errata _errata = Errata.None;
        private float _pointScaleFactor = 1.0f;
        private object? _context;

        private static readonly YGLogger DefaultLogger = (config, node, level, msg) => { /* Default logger implementation */ };

        public Config() : this(DefaultLogger) { }

        public Config(YGLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cloneNodeCallback = null;
        }

        public void SetUseWebDefaults(bool useWebDefaults)
        {
            _useWebDefaults = useWebDefaults;
        }

        public bool UseWebDefaults()
        {
            return _useWebDefaults;
        }

        public void SetExperimentalFeatureEnabled(ExperimentalFeature feature, bool enabled)
        {
            if (_experimentalFeatures.Test(feature) != enabled)
            {
                _experimentalFeatures.Set(feature, enabled);
                _version++;
            }
        }

        public bool IsExperimentalFeatureEnabled(ExperimentalFeature feature)
        {
            return _experimentalFeatures.Test(feature);
        }

        public ExperimentalFeatureSet GetEnabledExperiments()
        {
            return _experimentalFeatures;
        }

        public void SetErrata(Errata errata)
        {
            if (_errata != errata)
            {
                _errata = errata;
                _version++;
            }
        }

        public void AddErrata(Errata errata)
        {
            if (!HasErrata(errata))
            {
                _errata |= errata;
                _version++;
            }
        }

        public void RemoveErrata(Errata errata)
        {
            if (HasErrata(errata))
            {
                _errata &= (~errata);
                _version++;
            }
        }

        public Errata GetErrata()
        {
            return _errata;
        }

        public bool HasErrata(Errata errata)
        {
            return (_errata & errata) != Errata.None;
        }

        public void SetPointScaleFactor(float pointScaleFactor)
        {
            if (Math.Abs(_pointScaleFactor - pointScaleFactor) > float.Epsilon)
            {
                _pointScaleFactor = pointScaleFactor;
                _version++;
            }
        }

        public float GetPointScaleFactor()
        {
            return _pointScaleFactor;
        }

        public void SetContext(object? context)
        {
            _context = context;
        }

        public object? GetContext()
        {
            return _context;
        }

        public uint GetVersion()
        {
            return _version;
        }

        public void SetLogger(YGLogger logger)
        {
            _logger = logger;
        }

        public void Log(Node node, LogLevel logLevel, string format)
        {
            _logger(this, node, logLevel, format);
        }

        public void SetCloneNodeCallback(YGCloneNodeFunc? cloneNode)
        {
            _cloneNodeCallback = cloneNode;
        }

        public Node CloneNode(Node node, Node owner, int childIndex)
        {
            Node? clone = _cloneNodeCallback?.Invoke(node, owner, childIndex);
            return clone ?? YGNodeAPI.YGNodeClone(node);
        }

        public static Config Default => DefaultInstance.Value;

        public static Config GetDefault()
        {
            return DefaultInstance.Value;
        }

        private static readonly Lazy<Config> DefaultInstance = new Lazy<Config>(() => new Config(DefaultLogger));
    }

    internal static class ConfigExtensions
    {
        public static bool ConfigUpdateInvalidatesLayout(Config oldConfig, Config newConfig)
        {
            if (oldConfig == null) throw new ArgumentNullException(nameof(oldConfig));
            if (newConfig == null) throw new ArgumentNullException(nameof(newConfig));

            return oldConfig.GetErrata() != newConfig.GetErrata() ||
                   oldConfig.GetEnabledExperiments() != newConfig.GetEnabledExperiments() ||
                   oldConfig.GetPointScaleFactor() != newConfig.GetPointScaleFactor() ||
                   oldConfig.UseWebDefaults() != newConfig.UseWebDefaults();
        }
    }
}

