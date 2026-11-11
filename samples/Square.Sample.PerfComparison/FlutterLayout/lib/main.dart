import 'dart:async';
import 'dart:io';
import 'dart:math' as math;
import 'dart:ui' show FramePhase, FrameTiming;

import 'package:flutter/scheduler.dart';
import 'package:flutter/widgets.dart';

// 720-box layout-animation workload: the Flutter Windows counterpart of
// samples/Square.Sample.PerfComparison (Chrome/index.html,
// Square/CssAnimationPerfPage.sqv, Avalonia/Program.cs).
//
// Three groups of 240 boxes, every animation a 6s linear infinite triangle wave
// (start -> peak -> start):
//   transform (red #ef4444): three nested 30x30 layers animating
//                            translate 0->36->0, rotate 0->180->360deg and
//                            scale 0.6->1.4->0.6 around an 18x18 core with 6px
//                            padding. Paint-only: the 30x30 layout boxes never move.
//   opacity   (blue #3b82f6): 30x30 boxes, opacity 0->1->0. Paint-only.
//   width     (amber #f59e0b): 30px tall boxes, width 8->30->8. Every frame the
//                             Wrap re-packs its rows, i.e. a real layout pass.
//
// The window is a fixed 900x1240 client at DPR 1. After a 1s warmup the app
// counts presented frames with SchedulerBinding.addTimingsCallback for
// --seconds (default 30), prints one JSON line to stdout and exits.

const _boxSize = 30.0;
const _boxSpacing = 5.0;
const _boxRunSpacing = 4.0;
const _corePadding = 6.0; // 6 + 18 + 6 = 30: the 18x18 core, centered
const _boxesPerGroup = 240;
const _groupCount = 3;
const _loopDuration = Duration(seconds: 6); // every group loops 6s linear infinite
const _warmup = Duration(seconds: 1);

const _shiftMax = 36.0;
const _scaleMin = 0.6;
const _scaleMax = 1.4;
const _widthMin = 8.0;

const _background = Color(0xFFF3F6FB);
const _card = Color(0xFFFFFFFF);
const _headerBackground = Color(0xFFE8F1FF);
const _headerBorder = Color(0xFF2563EB);
const _cardBorder = Color(0xFF94A3B8);
const _text = Color(0xFF1E293B);
const _transformBox = Color(0xFFEF4444);
const _opacityBox = Color(0xFF3B82F6);
const _widthBox = Color(0xFFF59E0B);

const _fontFamily = 'Microsoft YaHei';

void main(List<String> args) {
  runApp(_BenchmarkApp(measureSeconds: _parseSeconds(args)));
}

/// Supports `--seconds 5` and `--seconds=5`; defaults to 30 for a missing,
/// malformed or non-positive value.
double _parseSeconds(List<String> args) {
  const fallback = 30.0;
  for (var i = 0; i < args.length; i++) {
    String? value;
    if (args[i] == '--seconds' && i + 1 < args.length) {
      value = args[i + 1];
    } else if (args[i].startsWith('--seconds=')) {
      value = args[i].substring('--seconds='.length);
    }
    final parsed = value == null ? null : double.tryParse(value);
    if (parsed != null && parsed.isFinite && parsed > 0) {
      return parsed;
    }
  }
  return fallback;
}

/// Triangle wave 0 -> 1 -> 0 over one loop.
double _triangle(double phase) =>
    phase <= 0.5 ? phase * 2 : 2 - phase * 2;

class _BenchmarkApp extends StatefulWidget {
  const _BenchmarkApp({required this.measureSeconds});

  final double measureSeconds;

  @override
  State<_BenchmarkApp> createState() => _BenchmarkAppState();
}

class _BenchmarkAppState extends State<_BenchmarkApp>
    with SingleTickerProviderStateMixin {
  late final AnimationController _loop;
  final Stopwatch _clock = Stopwatch()..start();
  Timer? _watchdog;

  int? _firstVsyncUs;
  int? _startVsyncUs;
  int _frames = 0;
  bool _finishing = false;

  int get _measureUs => (widget.measureSeconds * 1000000).round();

  @override
  void initState() {
    super.initState();
    _loop = AnimationController(
      vsync: this,
      duration: _loopDuration,
    )..repeat();
    SchedulerBinding.instance.addTimingsCallback(_onTimings);
    // Safety net: if the platform stops presenting frames (e.g. the window is
    // hidden), still finish instead of hanging forever.
    _watchdog = Timer.periodic(const Duration(seconds: 1), _onWatchdog);
  }

  @override
  void dispose() {
    _watchdog?.cancel();
    SchedulerBinding.instance.removeTimingsCallback(_onTimings);
    _loop.dispose();
    super.dispose();
  }

  /// Real per-frame telemetry: one [FrameTiming] per frame the engine produced.
  void _onTimings(List<FrameTiming> timings) {
    if (_finishing) {
      return;
    }
    for (final timing in timings) {
      final vsyncUs = timing.timestampInMicroseconds(FramePhase.vsyncStart);
      final startUs = _startVsyncUs;
      if (startUs == null) {
        final firstUs = _firstVsyncUs ??= vsyncUs;
        if (vsyncUs - firstUs >= _warmup.inMicroseconds) {
          _startVsyncUs = vsyncUs;
        }
        continue;
      }
      final elapsedUs = vsyncUs - startUs;
      if (elapsedUs >= _measureUs) {
        _finish(Duration(microseconds: elapsedUs));
        return;
      }
      _frames++;
    }
  }

  void _onWatchdog(Timer _) {
    if (_finishing) {
      return;
    }
    final elapsed = _clock.elapsed - _warmup;
    if (elapsed >= Duration(microseconds: _measureUs + 3000000)) {
      _finish(elapsed);
    }
  }

  Future<void> _finish(Duration window) async {
    if (_finishing) {
      return;
    }
    _finishing = true;
    _watchdog?.cancel();
    SchedulerBinding.instance.removeTimingsCallback(_onTimings);

    final view = WidgetsBinding.instance.platformDispatcher.views.first;
    final dpr = view.devicePixelRatio;
    final client = view.physicalSize / dpr;
    final seconds = window.inMicroseconds / 1000000;
    final fps = seconds > 0 ? _frames / seconds : 0.0;
    final dprText =
        dpr == dpr.roundToDouble() ? dpr.round().toString() : '$dpr';

    stdout.writeln(
      '{"target":"Flutter",'
      '"seconds":${seconds.toStringAsFixed(2)},'
      '"frames":$_frames,'
      '"fps":${fps.toStringAsFixed(1)},'
      '"client":"${client.width.round()}x${client.height.round()}",'
      '"dpr":$dprText,'
      '"boxes":${_boxesPerGroup * _groupCount},'
      '"groups":[${List.filled(_groupCount, _boxesPerGroup).join(',')}]}',
    );

    await stdout.flush();
    exit(0);
  }

  @override
  Widget build(BuildContext context) {
    return Directionality(
      textDirection: TextDirection.ltr,
      child: ColoredBox(
        color: _background,
        child: Padding(
          padding: const EdgeInsets.all(8),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const _Header(),
              _Card(
                title: 'transform：不触发布局（推荐）',
                child: AnimatedBuilder(
                  animation: _loop,
                  builder: (context, child) => _transformGroup(_loop.value),
                ),
              ),
              _Card(
                title: 'opacity：只触发合成与重绘',
                child: AnimatedBuilder(
                  animation: _loop,
                  builder: (context, child) => _opacityGroup(_loop.value),
                ),
              ),
              _Card(
                title: 'width：每帧触发布局（最贵）',
                child: AnimatedBuilder(
                  animation: _loop,
                  builder: (context, child) => _widthGroup(_loop.value),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  /// Group 1: three nested transform layers per box. Transforms are paint-time
  /// only, so the Wrap never re-lays-out.
  Widget _transformGroup(double phase) {
    final t = _triangle(phase);
    final shift = _shiftMax * t;
    final spin = phase * 2 * math.pi; // 0 -> 180 -> 360deg over one 6s loop
    final scale = _scaleMin + (_scaleMax - _scaleMin) * t;
    return _wrap(
      List<Widget>.generate(
        _boxesPerGroup,
        (i) => Transform.translate(
          offset: Offset(shift, 0),
          child: Transform.rotate(
            angle: spin,
            child: Transform.scale(
              scale: scale,
              child: const SizedBox(
                width: _boxSize,
                height: _boxSize,
                child: Padding(
                  padding: EdgeInsets.all(_corePadding),
                  child: ColoredBox(color: _transformBox),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  /// Group 2: opacity only.
  Widget _opacityGroup(double phase) {
    final opacity = _triangle(phase);
    return _wrap(
      List<Widget>.generate(
        _boxesPerGroup,
        (i) => Opacity(
          opacity: opacity,
          child: const SizedBox(
            width: _boxSize,
            height: _boxSize,
            child: ColoredBox(color: _opacityBox),
          ),
        ),
      ),
    );
  }

  /// Group 3: width changes every frame, so every wrap line re-packs.
  Widget _widthGroup(double phase) {
    final width = _widthMin + (_boxSize - _widthMin) * _triangle(phase);
    return _wrap(
      List<Widget>.generate(
        _boxesPerGroup,
        (i) => SizedBox(
          width: width,
          height: _boxSize,
          child: const ColoredBox(color: _widthBox),
        ),
      ),
    );
  }

  Widget _wrap(List<Widget> children) => Wrap(
    spacing: _boxSpacing,
    runSpacing: _boxRunSpacing,
    alignment: WrapAlignment.start,
    crossAxisAlignment: WrapCrossAlignment.start,
    children: children,
  );
}

class _Header extends StatelessWidget {
  const _Header();

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: _headerBackground,
        border: Border.all(color: _headerBorder, width: 2),
        borderRadius: BorderRadius.circular(8),
      ),
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'CSS 动画性能测试',
            style: TextStyle(
              fontFamily: _fontFamily,
              fontSize: 20,
              fontWeight: FontWeight.bold,
              color: _text,
            ),
          ),
          const SizedBox(height: 4),
          const Text(
            'MDN CSS 动画性能对照：Square / Avalonia / 原生 Chrome，各组 240 方块，'
            '6 秒循环；测量 30 秒 CPU 与 FPS。',
            style: TextStyle(fontFamily: _fontFamily, fontSize: 14, color: _text),
          ),
        ],
      ),
    );
  }
}

class _Card extends StatelessWidget {
  const _Card({required this.title, required this.child});

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(top: 5),
      padding: const EdgeInsets.all(5),
      decoration: BoxDecoration(
        color: _card,
        border: Border.all(color: _cardBorder),
        borderRadius: BorderRadius.circular(6),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            title,
            style: const TextStyle(
              fontFamily: _fontFamily,
              fontSize: 16,
              fontWeight: FontWeight.bold,
              color: _text,
            ),
          ),
          const SizedBox(height: 3),
          child,
        ],
      ),
    );
  }
}
