import 'dart:async';
import 'dart:convert' show jsonEncode;
import 'dart:io' show File, IOSink, exit, stderr, stdout;
import 'dart:math' as math show max;
import 'dart:ui' show FramePhase, FrameTiming;

import 'package:flutter/scheduler.dart' show SchedulerBinding;
import 'package:flutter/services.dart' show MethodChannel, StandardMethodCodec;
import 'package:flutter/widgets.dart';

/// Flutter Windows counterpart of the 720-box animation workload shared by
/// samples/Square.Sample.PerfComparison (Square CssAnimationPerfPage.sqv,
/// Avalonia BenchmarkWindow, Electron page). Three DISTINCT groups of 240
/// boxes, each animating one property class in a 6s linear infinite loop:
///   group 1  transform-only  translate 0->36->0 / rotate 0->180->360deg /
///            scale 0.6->1.4->0.6, three nested Transform layers around an
///            18x18 red core (#ef4444) inset 6px in the 30x30 box
///   group 2  opacity-only    0->1->0, blue #3b82f6
///   group 3  width-only      8->30->8 inside a Wrap, re-packing the wrapped
///            rows every frame (real widget/layout workload, not canvas), amber
///            #f59e0b
/// Boxes are 30x30 with a 5px right / 4px bottom gap inside three Wrap rows so
/// the packing matches the CSS flex-wrap grids (24 boxes per 900px line).
///
/// Measurement contract (parent override): 900x1240 logical client window at
/// outer origin (100,100), warmup (default 2s) then a configurable measured
/// window (default 30s). Frames are rasterized engine frames, not scanout:
/// delayed FrameTiming batches are filtered by rasterFinishWallTime against
/// the native CPU sample timestamps. The Windows engine produces that phase
/// with std::chrono::system_clock (Unix microseconds), unlike its monotonic
/// phase timestamps. Both interval boundaries come from the native channel.
/// CPU uses GetProcessTimes deltas (one logical core = 100%); memory samples
/// GetProcessMemoryInfo private commit / working set at about 1 Hz. GPU VRAM
/// is excluded. Failed native counters abort rather than reporting zeros.
/// The process exits automatically after writing the report.
void main(List<String> args) {
  BenchConfig config;
  try {
    config = BenchConfig.parse(args);
  } on FormatException catch (error) {
    _writeLine(stderr, 'square_flutter_perf: ${error.message}');
    _writeLine(stderr, BenchConfig.usage);
    _flushLater(stderr, exitCode: 2);
    return;
  }
  runApp(BenchmarkApp(config: config));
}

/// CLI contract shared with the Square/Avalonia/Electron targets:
///   --seconds <n>    measured window length in seconds (default 30)
///   --warmup <n>     warmup before the measured window in seconds (default 2)
///   --output <path>  write the compact JSON report to this file; when absent
///                    the same JSON is printed to stdout
class BenchConfig {
  const BenchConfig({
    required this.seconds,
    required this.warmupSeconds,
    required this.outputPath,
  });

  final double seconds;
  final double warmupSeconds;
  final String? outputPath;

  static const String usage =
      'usage: square_flutter_perf.exe [--seconds <n>] [--warmup <n>] '
      '[--output <path>]';

  static BenchConfig parse(List<String> args) {
    double? seconds;
    double? warmup;
    String? output;
    for (int i = 0; i < args.length; i++) {
      final String arg = args[i];
      final int eq = arg.indexOf('=');
      final String name = eq >= 0 ? arg.substring(0, eq) : arg;
      String? inline = eq >= 0 ? arg.substring(eq + 1) : null;
      String readValue(String flag) {
        if (inline != null) {
          final String value = inline!;
          inline = null;
          return value;
        }
        if (i + 1 >= args.length) {
          throw FormatException('$flag requires a value');
        }
        return args[++i];
      }

      switch (name) {
        case '--seconds':
          seconds = _parsePositive('--seconds', readValue('--seconds'));
        case '--warmup':
          warmup = _parseNonNegative('--warmup', readValue('--warmup'));
        case '--output':
          output = readValue('--output');
        default:
          throw FormatException('unknown argument: $arg');
      }
    }
    return BenchConfig(
      seconds: seconds ?? 30,
      warmupSeconds: warmup ?? 2,
      outputPath: output,
    );
  }

  static double _parsePositive(String flag, String raw) {
    final double? value = double.tryParse(raw);
    if (value == null || !value.isFinite || value <= 0) {
      throw FormatException('$flag must be a finite number > 0 (got "$raw")');
    }
    return value;
  }

  static double _parseNonNegative(String flag, String raw) {
    final double? value = double.tryParse(raw);
    if (value == null || !value.isFinite || value < 0) {
      throw FormatException('$flag must be a finite number >= 0 (got "$raw")');
    }
    return value;
  }
}

void _writeLine(IOSink sink, String message) {
  try {
    sink.writeln(message);
  } catch (_) {
    // A windowed process may not own a valid console stream; measurement and
    // report files are unaffected.
  }
}

Future<void> _flush(IOSink sink) async {
  try {
    await sink.flush();
  } catch (_) {
    // Nothing to flush without a valid stream.
  }
}

void _flushLater(IOSink sink, {required int exitCode}) {
  () async {
    await _flush(sink);
    exit(exitCode);
  }();
}

class BenchmarkApp extends StatelessWidget {
  const BenchmarkApp({super.key, required this.config});

  final BenchConfig config;

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.ltr,
      child: DefaultTextStyle(
        style: const TextStyle(
            fontFamily: 'Arial', fontSize: 16, color: Color(0xFF1E293B)),
        child: BenchmarkHarness(config: config),
      ),
    );
  }
}

class BenchmarkHarness extends StatefulWidget {
  const BenchmarkHarness({super.key, required this.config});

  final BenchConfig config;

  @override
  State<BenchmarkHarness> createState() => _BenchmarkHarnessState();
}

class _BenchmarkHarnessState extends State<BenchmarkHarness>
    with SingleTickerProviderStateMixin {
  static const int _microsecondsPerSecond = 1000 * 1000;
  static const int _bytesPerMiB = 1024 * 1024;
  static const int _targetFps = 60;
  static const Duration _drainTimeout = Duration(seconds: 3);

  static const Color _pageBackground = Color(0xFFF3F6FB);
  static const Color _textColor = Color(0xFF1E293B);
  static const Color _headerBackground = Color(0xFFE8F1FF);
  static const Color _headerBorder = Color(0xFF2563EB);
  static const Color _cardBorder = Color(0xFF94A3B8);
  static const Color _cardBackground = Color(0xFFFFFFFF);
  static const Color _transformCoreColor = Color(0xFFEF4444);
  static const Color _fadeColor = Color(0xFF3B82F6);
  static const Color _wobbleColor = Color(0xFFF59E0B);
  static const String _fontFamily = 'Arial';

  static const int _boxesPerGroup = 240;
  static const double _boxSide = 30;
  static const double _boxMarginRight = 5;
  static const double _boxMarginBottom = 4;
  static const double _transformCoreInset = 6; // 6 + 18 + 6 = 30
  static const double _shiftMax = 36;
  static const double _scaleMin = 0.6;
  static const double _scaleMax = 1.4;
  static const double _widthMin = 8;

  // Platform channel served by windows/runner/flutter_window.cpp.
  static const MethodChannel _benchChannel =
      MethodChannel('square.perf/bench', StandardMethodCodec());

  late final AnimationController _loop;
  late final Animation<double> _triangle;
  late final Animation<Offset> _shift;
  late final Animation<double> _scale;
  final Stopwatch _animationClock = Stopwatch();
  Timer? _animationTimer;

  Timer? _memoryTimer;
  Future<void>? _memoryTask;
  final List<int> _frameWallTimes = <int>[];
  final Completer<void> _timingsDrained = Completer<void>();
  int _measureStartWallUs = 0;
  int _measureEndWallUs = 0;
  int _latestFrameWallUs = 0;
  int _memorySamples = 0;
  int _privateSumBytes = 0;
  int _privatePeakBytes = 0;
  int _workingSumBytes = 0;
  int _workingPeakBytes = 0;
  bool _awaitingMemory = false;
  bool _failed = false;
  Size _clientSize = Size.zero;

  @override
  void initState() {
    super.initState();
    _loop = AnimationController(vsync: this, duration: const Duration(seconds: 6));
    _triangle = TweenSequence<double>([
      TweenSequenceItem(tween: Tween<double>(begin: 0, end: 1), weight: 1),
      TweenSequenceItem(tween: Tween<double>(begin: 1, end: 0), weight: 1),
    ]).animate(_loop);
    _shift = Tween<Offset>(
            begin: Offset.zero, end: const Offset(_shiftMax / _boxSide, 0))
        .animate(_triangle);
    _scale = Tween<double>(begin: _scaleMin, end: _scaleMax).animate(_triangle);
    _animationClock.start();
    _scheduleAnimationFrame();
    SchedulerBinding.instance.addTimingsCallback(_onTimingsReported);
    WidgetsBinding.instance.addPostFrameCallback((_) => _measure());
  }

  @override
  void dispose() {
    _animationTimer?.cancel();
    _animationClock.stop();
    _memoryTimer?.cancel();
    SchedulerBinding.instance.removeTimingsCallback(_onTimingsReported);
    _loop.dispose();
    super.dispose();
  }

  // Native vsync can exceed 60 Hz. Round waits up to whole milliseconds and
  // track absolute deadlines instead of using a drifting 16 ms periodic timer.
  void _scheduleAnimationFrame() {
    final int elapsed = _animationClock.elapsedMicroseconds;
    final int nextFrame = elapsed * _targetFps ~/ _microsecondsPerSecond + 1;
    final int remaining =
        nextFrame * _microsecondsPerSecond ~/ _targetFps - elapsed;
    _animationTimer = Timer(Duration(milliseconds: (remaining + 999) ~/ 1000), () {
      _loop.value = (_animationClock.elapsedMicroseconds %
              (6 * _microsecondsPerSecond)) /
          (6 * _microsecondsPerSecond);
      _scheduleAnimationFrame();
    });
  }

  @override
  Widget build(BuildContext context) {
    if (_clientSize == Size.zero) {
      _clientSize = MediaQuery.sizeOf(context);
    }
    return ColoredBox(
      color: _pageBackground,
      child: Align(
        alignment: Alignment.topLeft,
        child: Padding(
          padding: const EdgeInsets.all(8),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _buildHeader(),
              _buildSection(
                  'transform：不触发布局（推荐）', _buildGrid(_buildTransformBox)),
              _buildSection(
                  'opacity：只触发合成与重绘', _buildGrid(_buildFadeBox)),
              _buildSection('width：每帧触发布局（最贵）', _buildGrid(_buildWobbleBox)),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildHeader() {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: _headerBackground,
        border: Border.all(color: _headerBorder, width: 2),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'CSS 动画性能对比',
            style: TextStyle(
              fontSize: 20,
              fontWeight: FontWeight.w700,
              height: 1.15,
              color: _textColor,
              fontFamily: _fontFamily,
            ),
          ),
          const SizedBox(height: 4),
          const Text(
            '240 方块 × 3 组 · 6 秒循环 · 目标 60 FPS',
            style: TextStyle(
              fontSize: 16,
              height: 1.15,
              color: _textColor,
              fontFamily: _fontFamily,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildSection(String title, Widget grid) {
    return Container(
      margin: const EdgeInsets.only(top: 5),
      padding: const EdgeInsets.all(5),
      decoration: BoxDecoration(
        color: _cardBackground,
        border: Border.all(color: _cardBorder),
        borderRadius: BorderRadius.circular(6),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title,
            style: const TextStyle(
              fontSize: 16,
              fontWeight: FontWeight.w700,
              height: 1.15,
              color: _textColor,
              fontFamily: _fontFamily,
            ),
          ),
          const SizedBox(height: 3),
          grid,
        ],
      ),
    );
  }

  /// One Wrap per group with 240 boxes; each box carries its own 5px right /
  /// 4px bottom gap so the wrapped packing matches the CSS flex-wrap grids.
  Widget _buildGrid(Widget Function() buildBox) {
    return Wrap(
      children: List<Widget>.generate(_boxesPerGroup, (_) => buildBox()),
    );
  }

  /// Transform transitions retain their static layout boxes and colored cores.
  Widget _buildTransformBox() {
    return Padding(
      padding: const EdgeInsets.only(
          right: _boxMarginRight, bottom: _boxMarginBottom),
      child: SizedBox(
        width: _boxSide,
        height: _boxSide,
        child: SlideTransition(
          position: _shift,
          child: RotationTransition(
            turns: _loop,
            child: Padding(
              padding: const EdgeInsets.all(_transformCoreInset),
              child: ScaleTransition(
                scale: _scale,
                child: const ColoredBox(color: _transformCoreColor),
              ),
            ),
          ),
        ),
      ),
    );
  }

  /// Opacity changes only the retained opacity layer, not its static child.
  Widget _buildFadeBox() {
    return Padding(
      padding: const EdgeInsets.only(
          right: _boxMarginRight, bottom: _boxMarginBottom),
      child: FadeTransition(
        opacity: _triangle,
        child: const SizedBox(
          width: _boxSide,
          height: _boxSide,
          child: ColoredBox(color: _fadeColor),
        ),
      ),
    );
  }

  /// Changing actual child widths makes Wrap reflow its rows every frame.
  Widget _buildWobbleBox() {
    return Padding(
      padding: const EdgeInsets.only(
          right: _boxMarginRight, bottom: _boxMarginBottom),
      child: AnimatedBuilder(
        animation: _triangle,
        child: const ColoredBox(color: _wobbleColor),
        builder: (BuildContext context, Widget? child) => SizedBox(
          width: _widthMin + (_boxSide - _widthMin) * _triangle.value,
          height: _boxSide,
          child: child,
        ),
      ),
    );
  }

  // Timings can be delivered a second after rasterization. Retain their
  // timestamps until both native CPU interval boundaries are known, then
  // count only frames rasterized inside that exact interval.
  void _onTimingsReported(List<FrameTiming> timings) {
    for (final FrameTiming timing in timings) {
      final int timestamp =
          timing.timestampInMicroseconds(FramePhase.rasterFinishWallTime);
      _latestFrameWallUs = math.max(_latestFrameWallUs, timestamp);
      if (_measureStartWallUs == 0 || timestamp >= _measureStartWallUs) {
        _frameWallTimes.add(timestamp);
      }
    }
    if (_measureEndWallUs > 0 &&
        _latestFrameWallUs > _measureEndWallUs &&
        !_timingsDrained.isCompleted) {
      _timingsDrained.complete();
    }
  }

  Future<({double seconds, int wallMicros})> _readCpu() async {
    final Map<String, Object?>? values =
        await _benchChannel.invokeMapMethod<String, Object?>('cpuTimes');
    return (
      seconds: (values!['kernelSeconds']! as num).toDouble() +
          (values['userSeconds']! as num).toDouble(),
      wallMicros: values['sampledWallMicros']! as int,
    );
  }

  Future<void> _sampleMemory() async {
    _awaitingMemory = true;
    try {
      final Map<String, Object?>? values =
          await _benchChannel.invokeMapMethod<String, Object?>('memoryInfo');
      final int privateBytes = values!['privateBytes']! as int;
      final int workingBytes = values['workingSetBytes']! as int;
      _privateSumBytes += privateBytes;
      _workingSumBytes += workingBytes;
      _privatePeakBytes = math.max(_privatePeakBytes, privateBytes);
      _workingPeakBytes = math.max(_workingPeakBytes, workingBytes);
      _memorySamples++;
    } catch (error) {
      _abort('memory sampling failed: $error');
    } finally {
      _awaitingMemory = false;
    }
  }

  Future<void> _measure() async {
    try {
      await Future<void>.delayed(Duration(
          microseconds:
              (widget.config.warmupSeconds * _microsecondsPerSecond).round()));
      final start = await _readCpu();
      _measureStartWallUs = start.wallMicros;
      _frameWallTimes.removeWhere((timestamp) => timestamp < _measureStartWallUs);
      _memoryTask = _sampleMemory();
      _memoryTimer = Timer.periodic(const Duration(seconds: 1), (_) {
        if (!_awaitingMemory) {
          _memoryTask = _sampleMemory();
        }
      });
      await Future<void>.delayed(Duration(
          microseconds:
              (widget.config.seconds * _microsecondsPerSecond).round()));
      _memoryTimer?.cancel();
      await _memoryTask;
      if (_failed) return;
      final end = await _readCpu();
      _measureEndWallUs = end.wallMicros;
      if (_latestFrameWallUs > _measureEndWallUs) {
        _timingsDrained.complete();
      }
      await _timingsDrained.future.timeout(_drainTimeout);
      SchedulerBinding.instance.removeTimingsCallback(_onTimingsReported);
      _animationTimer?.cancel();
      _animationClock.stop();

      int frames = 0;
      for (final int timestamp in _frameWallTimes) {
        if (timestamp >= _measureStartWallUs && timestamp < _measureEndWallUs) {
          frames++;
        }
      }
      final double seconds =
          (_measureEndWallUs - _measureStartWallUs) / _microsecondsPerSecond;
      final double cpuSeconds = end.seconds - start.seconds;
      if (frames == 0 || _memorySamples == 0 || seconds <= 0 || cpuSeconds < 0) {
        throw StateError('Incomplete frame/process measurement');
      }
      final Map<String, Object?> report = <String, Object?>{
        'target': 'Flutter Windows Release',
        'seconds': seconds,
        'frames': frames,
        'fps': frames / seconds,
        'fpsDefinition': 'Rasterized Flutter engine FrameTiming records per '
            'native process measurement second; not physical scanout',
        'cpuSeconds': cpuSeconds,
        'cpuOneCorePercent': cpuSeconds / seconds * 100,
        'privateAvgMiB': _privateSumBytes / _memorySamples / _bytesPerMiB,
        'privatePeakMiB': _privatePeakBytes / _bytesPerMiB,
        'workingAvgMiB': _workingSumBytes / _memorySamples / _bytesPerMiB,
        'workingPeakMiB': _workingPeakBytes / _bytesPerMiB,
        'samples': _memorySamples,
        'clientWidth': _clientSize.width,
        'clientHeight': _clientSize.height,
        'warmupSeconds': widget.config.warmupSeconds,
        'targetFps': _targetFps,
        'startedUtc': DateTime.fromMicrosecondsSinceEpoch(_measureStartWallUs,
                isUtc: true)
            .toIso8601String(),
      };
      final String json = jsonEncode(report);
      final String? outputPath = widget.config.outputPath;
      if (outputPath != null) {
        final File file = File(outputPath);
        file.parent.createSync(recursive: true);
        file.writeAsStringSync('$json\n', flush: true);
      }
      _writeLine(stdout, json);
      await _flush(stdout);
      exit(0);
    } catch (error) {
      _abort('measurement failed: $error');
    }
  }

  void _abort(String message) {
    if (_failed) return;
    _failed = true;
    _memoryTimer?.cancel();
    SchedulerBinding.instance.removeTimingsCallback(_onTimingsReported);
    _animationTimer?.cancel();
    _animationClock.stop();
    _writeLine(stderr, 'square_flutter_perf: $message');
    _flushLater(stderr, exitCode: 3);
  }
}

