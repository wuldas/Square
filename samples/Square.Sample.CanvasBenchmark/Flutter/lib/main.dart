import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/rendering.dart';
import 'package:flutter/scheduler.dart';
import 'package:flutter/widgets.dart';

typedef DrawCommand = void Function(Canvas canvas);

class Config {
  Config(List<String> args) {
    for (var i = 0; i < args.length; i++) {
      if (args[i] == '--screenshot') { screenshot = true; continue; }
      if (!['--scene', '--output', '--load', '--seconds', '--warmup'].contains(args[i]) || i + 1 == args.length) {
        throw FormatException('Invalid argument ${args[i]}');
      }
      final name = args[i];
      final value = args[++i];
      switch (name) {
        case '--scene': scene = value;
        case '--output': output = value;
        case '--load': load = int.parse(value);
        case '--seconds': seconds = double.parse(value);
        case '--warmup': warmup = double.parse(value);
      }
    }
    if (scene.isEmpty || output.isEmpty || load < 1 || !seconds.isFinite || seconds <= 0 || !warmup.isFinite || warmup < 0) {
      throw const FormatException('Require --scene, --output, positive --load/--seconds and nonnegative --warmup');
    }
  }
  String scene = '', output = '';
  int load = 1;
  double seconds = 10, warmup = 2;
  bool screenshot = false;
}

Color parseColor(String value) {
  final hex = value.substring(1);
  final rgba = int.parse(hex.length == 6 ? '${hex}ff' : hex, radix: 16);
  return Color(((rgba & 255) << 24) | (rgba >> 8));
}

class PreparedScene {
  PreparedScene(Map<String, dynamic> data, ui.Image image) {
    width = (data['width'] as num).toDouble();
    height = (data['height'] as num).toDouble();
    background = Paint()..color = parseColor(data['background']);
    frames = (data['frames'] as List).map((f) => (f['ops'] as List).map((op) => prepare(op as List, image)).toList()).toList();
  }
  late final double width, height;
  late final Paint background;
  late final List<List<DrawCommand>> frames;
  final Map<String, Paint> paints = {};
  final Map<String, TextPainter> texts = {};
  final Paint imagePaint = Paint()..isAntiAlias = true..filterQuality = FilterQuality.low;
  Paint paint(String color, [double width = 0, StrokeCap cap = StrokeCap.butt]) => paints.putIfAbsent('$color/$width/$cap', () => Paint()
    ..color = parseColor(color)..isAntiAlias = true..strokeWidth = width..strokeCap = cap
    ..style = width == 0 ? PaintingStyle.fill : PaintingStyle.stroke);

  DrawCommand prepare(List op, ui.Image image) {
    double n(int i) => (op[i] as num).toDouble();
    switch (op[0]) {
      case 'line':
        final a = Offset(n(1), n(2)), b = Offset(n(3), n(4)), p = paint(op[5], n(6));
        return (canvas) => canvas.drawLine(a, b, p);
      case 'rect':
        final rect = Rect.fromLTWH(n(1), n(2), n(3), n(4)), p = paint(op[5]);
        return (canvas) => canvas.drawRect(rect, p);
      case 'circle':
        final center = Offset(n(1), n(2)), radius = n(3), p = paint(op[4], n(5));
        return (canvas) => canvas.drawCircle(center, radius, p);
      case 'arc':
        final rect = Rect.fromCircle(center: Offset(n(1), n(2)), radius: n(3));
        final start = n(4), sweep = n(5), p = paint(op[6], n(7));
        final capPaint = paint(op[6]), capRadius = n(7) / 2;
        final a = Offset(n(1) + n(3) * math.cos(start), n(2) + n(3) * math.sin(start));
        final b = Offset(n(1) + n(3) * math.cos(start + sweep), n(2) + n(3) * math.sin(start + sweep));
        return (canvas) {
          canvas.drawArc(rect, start, sweep, false, p);
          canvas.drawCircle(a, capRadius, capPaint); canvas.drawCircle(b, capRadius, capPaint);
        };
      case 'curve':
        // Shared contract: prepare the same 32-segment cubic polyline.
        final path = Path();
        for (var i = 0; i <= 32; i++) {
          final t = i / 32, u = 1 - t;
          final x = u*u*u*n(1) + 3*u*u*t*n(3) + 3*u*t*t*n(5) + t*t*t*n(7);
          final y = u*u*u*n(2) + 3*u*u*t*n(4) + 3*u*t*t*n(6) + t*t*t*n(8);
          if (i == 0) { path.moveTo(x, y); } else { path.lineTo(x, y); }
        }
        final fill = Path.from(path)..lineTo(n(7), n(9))..lineTo(n(1), n(9))..close();
        final fillPaint = paints.putIfAbsent('gradient/${op[10]}/${op[11]}', () => Paint()
          ..isAntiAlias = true
          ..shader = ui.Gradient.linear(const Offset(0, 210), const Offset(0, 390), [parseColor(op[10]), parseColor(op[11])]));
        final stroke = paint(op[10], n(12));
        final gradientRect = Rect.fromLTWH(n(1), 200, n(7) - n(1), 190);
        return (canvas) {
          canvas.save(); canvas.clipPath(fill); canvas.drawRect(gradientRect, fillPaint); canvas.restore();
          canvas.drawPath(path, stroke);
        };
      case 'text':
        final key = '${op[1]}/${op[4]}/${op[5]}';
        final text = texts.putIfAbsent(key, () => TextPainter(
          text: TextSpan(text: op[1], style: TextStyle(fontFamily: 'Segoe UI', fontWeight: FontWeight.w400, fontSize: n(4), color: parseColor(op[5]))),
          textDirection: TextDirection.ltr,
          textScaler: TextScaler.noScaling,
        )..layout());
        final offset = Offset(n(2), n(3));
        return (canvas) => text.paint(canvas, offset);
      case 'image':
        final source = Rect.fromLTWH(0, 0, image.width.toDouble(), image.height.toDouble());
        final target = Rect.fromLTWH(n(1), n(2), n(3), n(4));
        return (canvas) => canvas.drawImageRect(image, source, target, imagePaint);
      default: throw FormatException('Unknown op ${op[0]}');
    }
  }
}

Future<void> main(List<String> args) async {
  Config? config;
  try {
    config = Config(args);
    WidgetsFlutterBinding.ensureInitialized();
    final data = jsonDecode(File(config.scene).readAsStringSync()) as Map<String, dynamic>;
    final imageFile = File('${File(config.scene).parent.path}${Platform.pathSeparator}icon.png');
    final codec = await ui.instantiateImageCodec(await imageFile.readAsBytes());
    final image = (await codec.getNextFrame()).image;
    codec.dispose();
    final scene = PreparedScene(data, image);
    runApp(Directionality(textDirection: TextDirection.ltr, child: Benchmark(config: config, scene: scene)));
  } catch (error, stack) {
    if (config != null) { File('${config.output}.error.txt').writeAsStringSync('$error\n$stack'); }
    exit(1);
  }
}

class PaintClock extends ChangeNotifier {
  final Stopwatch elapsed = Stopwatch()..start();
  int startUs = 0, endUs = 0, uiPaintCount = 0;
  final List<double> uiDrawTimes = [];
  bool get measuring => startUs > 0;
  void tick() => notifyListeners();
}

class ScenePainter extends CustomPainter {
  ScenePainter(this.scene, this.config, this.clock) : super(repaint: clock);
  final PreparedScene scene;
  final Config config;
  final PaintClock clock;
  @override
  void paint(Canvas canvas, Size size) {
    final wall = DateTime.now().microsecondsSinceEpoch;
    final watch = Stopwatch()..start();
    canvas.drawRect(Rect.fromLTWH(0, 0, scene.width, scene.height), scene.background);
    final commands = scene.frames[(clock.elapsed.elapsedMicroseconds * 60 ~/ 1000000) % scene.frames.length];
    for (var i = 0; i < config.load; i++) { for (final draw in commands) { draw(canvas); } }
    watch.stop();
    if (clock.measuring && wall >= clock.startUs && wall < clock.endUs) {
      clock.uiPaintCount++;
      clock.uiDrawTimes.add(watch.elapsedMicroseconds / 1000);
    }
  }
  @override
  bool shouldRepaint(ScenePainter oldDelegate) => true;
}

class Benchmark extends StatefulWidget {
  const Benchmark({super.key, required this.config, required this.scene});
  final Config config;
  final PreparedScene scene;
  @override
  State<Benchmark> createState() => _BenchmarkState();
}

class _BenchmarkState extends State<Benchmark> with SingleTickerProviderStateMixin {
  final PaintClock clock = PaintClock();
  final GlobalKey boundaryKey = GlobalKey();
  final List<ui.FrameTiming> timings = [];
  final Completer<void> drained = Completer<void>();
  late final Ticker ticker;
  late final ScenePainter painter;
  @override
  void initState() {
    super.initState();
    painter = ScenePainter(widget.scene, widget.config, clock);
    SchedulerBinding.instance.addTimingsCallback(onTimings);
    ticker = createTicker((_) => clock.tick())..start();
    WidgetsBinding.instance.addPostFrameCallback((_) => measure());
  }
  void onTimings(List<ui.FrameTiming> batch) {
    timings.addAll(batch);
    if (clock.endUs > 0 && batch.any((t) => t.timestampInMicroseconds(ui.FramePhase.rasterFinishWallTime) >= clock.endUs) && !drained.isCompleted) {
      drained.complete();
    }
  }
  Future<void> measure() async {
    try {
      await Future<void>.delayed(Duration(microseconds: (widget.config.warmup * 1000000).round()));
      clock.startUs = DateTime.now().microsecondsSinceEpoch;
      clock.endUs = clock.startUs + (widget.config.seconds * 1000000).round();
      await Future<void>.delayed(Duration(microseconds: (widget.config.seconds * 1000000).round()));
      // Release builds batch timings. Continue scheduling to drain a batch beyond the cutoff.
      await drained.future.timeout(const Duration(seconds: 4));
      ticker.stop();
      SchedulerBinding.instance.removeTimingsCallback(onTimings);
      final measured = timings.where((t) {
        final wall = t.timestampInMicroseconds(ui.FramePhase.rasterFinishWallTime);
        return wall >= clock.startUs && wall < clock.endUs;
      }).toList()..sort((a, b) => a.timestampInMicroseconds(ui.FramePhase.rasterFinish).compareTo(b.timestampInMicroseconds(ui.FramePhase.rasterFinish)));
      if (measured.isEmpty) { throw StateError('No rasterized frames in measurement window'); }
      final intervals = <double>[];
      for (var i = 1; i < measured.length; i++) {
        intervals.add((measured[i].timestampInMicroseconds(ui.FramePhase.rasterFinish) - measured[i-1].timestampInMicroseconds(ui.FramePhase.rasterFinish)) / 1000);
      }
      final view = View.of(context);
      final result = {
        'framework': 'Flutter', 'load': widget.config.load,
        'frames': measured.length, 'seconds': widget.config.seconds, 'fps': measured.length / widget.config.seconds,
        'frameIntervalsMs': intervals,
        'drawTimesMs': measured.map((t) => t.rasterDuration.inMicroseconds / 1000).toList(),
        'uiPaintCount': clock.uiPaintCount, 'uiDrawTimesMs': clock.uiDrawTimes,
        'clientWidth': view.physicalSize.width / view.devicePixelRatio,
        'clientHeight': view.physicalSize.height / view.devicePixelRatio, 'devicePixelRatio': view.devicePixelRatio,
        'versions': {
          'flutter': const String.fromEnvironment('CANVAS_FLUTTER_VERSION', defaultValue: '3.47.7'),
          'engine': const String.fromEnvironment('CANVAS_ENGINE_REVISION'),
          'dart': Platform.version, 'os': Platform.operatingSystemVersion, 'mode': 'release',
          'renderer': 'Flutter Windows default pipeline (backend not queried)',
        },
        'metric': 'Rasterized engine FrameTiming frames filtered by rasterFinishWallTime in the configured wall-clock window; intervals use monotonic rasterFinish; drawTimesMs is rasterDuration, uiDrawTimesMs is CustomPainter command submission; physical scanout is not observed',
      };
      final output = File(widget.config.output);
      await output.parent.create(recursive: true);
      await output.writeAsString(const JsonEncoder.withIndent('  ').convert(result));
      if (widget.config.screenshot) {
        await WidgetsBinding.instance.endOfFrame;
        final boundary = boundaryKey.currentContext!.findRenderObject()! as RenderRepaintBoundary;
        final image = await boundary.toImage(pixelRatio: view.devicePixelRatio);
        final png = await image.toByteData(format: ui.ImageByteFormat.png);
        await File('${widget.config.output}.png').writeAsBytes(png!.buffer.asUint8List());
        image.dispose();
      }
      exit(0);
    } catch (error, stack) {
      await File('${widget.config.output}.error.txt').writeAsString('$error\n$stack');
      exit(1);
    }
  }
  @override
  Widget build(BuildContext context) => RepaintBoundary(key: boundaryKey, child: CustomPaint(painter: painter, willChange: true, child: const SizedBox.expand()));
  @override
  void dispose() {
    ticker.dispose();
    SchedulerBinding.instance.removeTimingsCallback(onTimings);
    clock.dispose();
    super.dispose();
  }
}
