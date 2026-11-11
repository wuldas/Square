import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:crypto/crypto.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/scheduler.dart';
import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';

const background = Color(0xff101827);
const fontFamily = 'CanvasBench';
const supportedOpcodes = {
  'poly',
  'poly-new',
  'stroke',
  'stroke-new',
  'rect',
  'text',
  'text-new',
  'image',
  'transform',
  'end-transform',
  'clip',
  'clip-new',
  'end-clip',
  'layer',
  'end-layer',
  'gradient',
};
final startup = Stopwatch()..start();
Config? activeConfig;
bool failing = false;

typedef DrawCommand = void Function(Canvas canvas);

class Config {
  Config(List<String> args) {
    if (args.length.isOdd) {
      throw const FormatException('All CLI arguments require a value');
    }
    final values = <String, String>{};
    for (var i = 0; i < args.length; i += 2) {
      final name = args[i];
      if (!{'--trace', '--output', '--tag', '--verify'}.contains(name) ||
          values.containsKey(name)) {
        throw FormatException('Unknown or duplicate argument: $name');
      }
      values[name] = args[i + 1];
    }
    trace = values['--trace'] ?? '';
    output = values['--output'] ?? '';
    tag = int.parse(values['--tag'] ?? '0');
    final verifyValue = values['--verify'] ?? '0';
    if (!File(trace).isAbsolute ||
        !File(output).isAbsolute ||
        tag < 1 ||
        tag > 255 ||
        !{'0', '1'}.contains(verifyValue)) {
      throw const FormatException(
        'Require absolute --trace/--output, --tag 1..255, and --verify 0|1',
      );
    }
    verify = verifyValue == '1';
  }
  late final String trace, output;
  late final int tag;
  late final bool verify;
}

Color parseColor(String value) {
  if (!RegExp(r'^#[0-9a-fA-F]{8}$').hasMatch(value)) {
    throw FormatException('Invalid RGBA: $value');
  }
  final rgba = int.parse(value.substring(1), radix: 16);
  return Color(((rgba & 255) << 24) | (rgba >> 8));
}

Path makePath(List<dynamic> points, {required bool close}) {
  final path = Path();
  for (var i = 0; i < points.length; i++) {
    final p = points[i] as List;
    final x = (p[0] as num).toDouble(), y = (p[1] as num).toDouble();
    if (i == 0) {
      path.moveTo(x, y);
    } else {
      path.lineTo(x, y);
    }
  }
  if (close) path.close();
  return path;
}

TextPainter makeText(String text, double size, Color color) => TextPainter(
  text: TextSpan(
    text: text,
    style: TextStyle(
      fontFamily: fontFamily,
      fontSize: size,
      fontWeight: FontWeight.w400,
      color: color,
    ),
  ),
  textDirection: TextDirection.ltr,
  textScaler: TextScaler.noScaling,
)..layout();

class PreparedFrame {
  final commands = <DrawCommand>[];
  final dynamicText = <TextPainter>[];
  void dispose() {
    for (final text in dynamicText) {
      text.dispose();
    }
  }
}

class TraceScene {
  TraceScene(
    this.data,
    this.traceSha256,
    this.image,
    this.fontSha256,
    this.imageSha256,
  ) {
    if (data['protocolVersion'] != 11 ||
        data['width'] != 1280 ||
        data['height'] != 800) {
      throw const FormatException('Require protocolVersion=11 and 1280x800');
    }
    warmupFrames = data['warmupFrames'] as int;
    measureFrames = data['measureFrames'] as int;
    frames = (data['frames'] as List)
        .map(
          (f) => ((f as Map)['ops'] as List)
              .map((op) => op as List<dynamic>)
              .toList(),
        )
        .toList();
    if (warmupFrames < 0 ||
        measureFrames < 1 ||
        warmupFrames > 65535 ||
        measureFrames > 65535 ||
        frames.length != warmupFrames + measureFrames) {
      throw const FormatException('Invalid warmup/measurement frame counts');
    }
    final counts = <String, int>{};
    for (var i = 0; i < frames.length; i++) {
      final stack = <String>[];
      for (final op in frames[i]) {
        final code = op[0] as String;
        if (!supportedOpcodes.contains(code)) {
          throw FormatException('Unknown opcode $code');
        }
        if (i >= warmupFrames) {
          counts.update(code, (n) => n + 1, ifAbsent: () => 1);
        }
        if (code == 'transform' ||
            code == 'clip' ||
            code == 'clip-new' ||
            code == 'layer') {
          stack.add(code == 'clip-new' ? 'clip' : code);
        } else if (code.startsWith('end-')) {
          if (stack.isEmpty || stack.removeLast() != code.substring(4)) {
            throw FormatException('Unbalanced opcode $code in frame $i');
          }
        }
      }
      if (stack.isNotEmpty) throw FormatException('Unclosed state in frame $i');
    }
    expectedCounts = Map<String, int>.from(
      data['expectedMeasuredOpcodeTotals'] as Map,
    );
    if (!mapEquals(normalizeCounts(counts), normalizeCounts(expectedCounts))) {
      throw FormatException(
        'Trace opcode declaration differs from its measured inputs: $counts != $expectedCounts',
      );
    }
  }
  final Map<String, dynamic> data;
  final String traceSha256, fontSha256, imageSha256;
  final ui.Image image;
  late final int warmupFrames, measureFrames;
  late final List<List<List<dynamic>>> frames;
  late final Map<String, int> expectedCounts;
  final staticPaths = <String, Path>{};
  final staticText = <String, TextPainter>{};
  final backgroundPaint = Paint()..color = background;
  final imagePaint = Paint()
    ..filterQuality = FilterQuality.none
    ..isAntiAlias = false;
  late final Rect imageSource = Rect.fromLTWH(
    0,
    0,
    image.width.toDouble(),
    image.height.toDouble(),
  );

  void prepareStatic() {
    if ((data['samplingProfile'] as String? ?? '').contains('native-scaled')) {
      imagePaint.filterQuality = FilterQuality.low;
    }
    for (final ops in frames) {
      for (final op in ops) {
        final code = op[0];
        if (code == 'poly' || code == 'clip' || code == 'stroke') {
          final closed = code != 'stroke';
          final key = '${op[1]}/$closed';
          staticPaths.putIfAbsent(
            key,
            () =>
                makePath((data['paths'] as List)[op[1]] as List, close: closed),
          );
        } else if (code == 'text') {
          final text = (data['texts'] as List)[op[1]] as Map;
          staticText.putIfAbsent(
            '${op[1]}/${op[4]}',
            () => makeText(
              text['text'] as String,
              (text['size'] as num? ?? 14).toDouble(),
              parseColor(op[4] as String),
            ),
          );
        }
      }
    }
  }

  PreparedFrame prepareFrame(int index) {
    final result = PreparedFrame();
    for (final op in frames[index]) {
      double n(int i) => (op[i] as num).toDouble();
      Paint paint(String value) => Paint()
        ..color = parseColor(value)
        ..isAntiAlias = true;
      switch (op[0]) {
        case 'poly':
        case 'poly-new':
        case 'stroke':
        case 'stroke-new':
          final code = op[0] as String;
          final stroke = code.startsWith('stroke');
          final path = code.endsWith('-new')
              ? makePath(op[1] as List, close: !stroke)
              : staticPaths['${op[1]}/${!stroke}']!;
          final p = paint(op[2] as String);
          if (stroke) {
            p.style = PaintingStyle.stroke;
            p.strokeWidth = n(3);
          }
          result.commands.add((canvas) => canvas.drawPath(path, p));
        case 'rect':
          final rect = Rect.fromLTWH(n(1), n(2), n(3), n(4)),
              p = paint(op[5] as String);
          result.commands.add((canvas) => canvas.drawRect(rect, p));
        case 'text':
          final text = staticText['${op[1]}/${op[4]}']!,
              offset = Offset(n(2), n(3));
          result.commands.add((canvas) => text.paint(canvas, offset));
        case 'text-new':
          final text = makeText(
                op[1] as String,
                n(2),
                parseColor(op[5] as String),
              ),
              offset = Offset(n(3), n(4));
          result.dynamicText.add(text);
          result.commands.add((canvas) => text.paint(canvas, offset));
        case 'image':
          final target = Rect.fromLTWH(n(1), n(2), n(3), n(4));
          result.commands.add(
            (canvas) =>
                canvas.drawImageRect(image, imageSource, target, imagePaint),
          );
        case 'transform':
          final matrix = Float64List.fromList([
            n(1),
            n(2),
            0,
            0,
            n(3),
            n(4),
            0,
            0,
            0,
            0,
            1,
            0,
            n(5),
            n(6),
            0,
            1,
          ]);
          result.commands.add((canvas) {
            canvas.save();
            canvas.transform(matrix);
          });
        case 'clip':
        case 'clip-new':
          final path = op[0] == 'clip-new'
              ? makePath(op[1] as List, close: true)
              : staticPaths['${op[1]}/true']!;
          result.commands.add((canvas) {
            canvas.save();
            canvas.clipPath(path);
          });
        case 'layer':
          final rect = Rect.fromLTWH(n(1), n(2), n(3), n(4));
          final p = Paint()
            ..color = const Color(0xffffffff).withValues(alpha: n(5));
          result.commands.add((canvas) => canvas.saveLayer(rect, p));
        case 'end-transform':
        case 'end-clip':
        case 'end-layer':
          result.commands.add((canvas) => canvas.restore());
        case 'gradient':
          final rect = Rect.fromLTWH(n(1), n(2), n(3), n(4));
          final p = Paint()
            ..isAntiAlias = true
            ..shader = ui.Gradient.linear(
              Offset(n(5), n(6)),
              Offset(n(7), n(8)),
              [parseColor(op[9] as String), parseColor(op[10] as String)],
              null,
              TileMode.clamp,
            );
          result.commands.add((canvas) => canvas.drawRect(rect, p));
      }
    }
    return result;
  }
}

Map<String, int> normalizeCounts(Map<String, int> counts) =>
    Map.fromEntries(counts.entries.where((e) => e.value != 0));

void drawMarker(Canvas canvas, int tag, int phase, int seq) {
  final payload = ((tag & 255) << 18) | ((phase & 3) << 16) | (seq & 65535);
  var crc = 0;
  for (var i = 0; i < 4; i++) {
    crc ^= (payload >> (8 * i)) & 255;
    for (var bit = 0; bit < 8; bit++) {
      crc = ((crc << 1) ^ ((crc & 128) != 0 ? 7 : 0)) & 255;
    }
  }
  final bits = 0xa5 | (payload << 8) | (crc << 40);
  final black = Paint()
    ..color = const Color(0xff000000)
    ..isAntiAlias = false;
  final white = Paint()
    ..color = const Color(0xffffffff)
    ..isAntiAlias = false;
  for (var cell = 0; cell < 48; cell++) {
    canvas.drawRect(
      Rect.fromLTWH(cell * 2.0, 0, 2, 8),
      ((bits >> cell) & 1) == 0 ? black : white,
    );
  }
}

Future<void> fail(Object error, StackTrace stack) async {
  if (failing) return;
  failing = true;
  stderr.writeln('$error\n$stack');
  final config = activeConfig;
  if (config != null) {
    final output = File(config.output);
    await output.parent.create(recursive: true);
    await output.writeAsString(
      const JsonEncoder.withIndent('  ').convert({
        'protocolVersion': 11,
        'framework': 'Flutter',
        'status': 'failed',
        'trace': config.trace,
        'tag': config.tag,
        'error': '$error',
        'stack': '$stack',
      }),
    );
    await File('${config.output}.error.txt').writeAsString('$error\n$stack');
  }
  exit(1);
}

Future<void> main(List<String> args) async {
  try {
    final config = activeConfig = Config(args);
    WidgetsFlutterBinding.ensureInitialized();
    FlutterError.onError = (details) =>
        unawaited(fail(details.exception, details.stack ?? StackTrace.current));
    ui.PlatformDispatcher.instance.onError = (error, stack) {
      unawaited(fail(error, stack));
      return true;
    };
    final inputWatch = Stopwatch()..start();
    final traceFile = File(config.trace),
        traceBytes = await File(config.trace).readAsBytes();
    final data = jsonDecode(utf8.decode(traceBytes)) as Map<String, dynamic>;
    final inputLoadMs = inputWatch.elapsedMicroseconds / 1000;
    final assetWatch = Stopwatch()..start();
    final fontBytes = await File('${traceFile.parent.path}/font.ttf')
        .readAsBytes();
    final loader = FontLoader(fontFamily)
      ..addFont(Future<ByteData>.value(ByteData.sublistView(fontBytes)));
    await loader.load();
    final imageBytes = await File('${traceFile.parent.path}/image.png')
        .readAsBytes();
    final codec = await ui.instantiateImageCodec(imageBytes);
    final image = (await codec.getNextFrame()).image;
    codec.dispose();
    final assetLoadMs = assetWatch.elapsedMicroseconds / 1000;
    final prepareWatch = Stopwatch()..start();
    final scene = TraceScene(
      data,
      sha256.convert(traceBytes).toString(),
      image,
      sha256.convert(fontBytes).toString(),
      sha256.convert(imageBytes).toString(),
    );
    final validationMs = prepareWatch.elapsedMicroseconds / 1000;
    prepareWatch.reset();
    scene.prepareStatic();
    final staticPrepareMs = prepareWatch.elapsedMicroseconds / 1000;
    runApp(
      Directionality(
        textDirection: TextDirection.ltr,
        child: Benchmark(
          config: config,
          scene: scene,
          startupMetrics: {
            'inputLoadMs': inputLoadMs,
            'assetLoadMs': assetLoadMs,
            'traceValidationMs': validationMs,
            'staticPrepareMs': staticPrepareMs,
            'assetsReadySinceProcessEntryMs':
                startup.elapsedMicroseconds / 1000,
            'rssAfterStaticPrepareBytes': ProcessInfo.currentRss,
          },
        ),
      ),
    );
  } catch (error, stack) {
    await fail(error, stack);
  }
}

class PaintClock extends ChangeNotifier {
  void tick() => notifyListeners();
}

class _TracePainter extends CustomPainter {
  _TracePainter(this.owner) : super(repaint: owner.clock);
  final _BenchmarkState owner;
  @override
  void paint(Canvas canvas, Size size) => owner.paintTrace(canvas, size);
  @override
  bool shouldRepaint(_TracePainter oldDelegate) => false;
}

class Benchmark extends StatefulWidget {
  const Benchmark({
    super.key,
    required this.config,
    required this.scene,
    required this.startupMetrics,
  });
  final Config config;
  final TraceScene scene;
  final Map<String, dynamic> startupMetrics;
  @override
  State<Benchmark> createState() => _BenchmarkState();
}

class _BenchmarkState extends State<Benchmark> {
  final clock = PaintClock();
  final boundaryKey = GlobalKey();
  final timings = <Map<String, dynamic>>[];
  final frameCpu = <Map<String, dynamic>>[];
  final warmupCpu = <Map<String, dynamic>>[];
  final producerFrames = <Map<String, dynamic>>[];
  final actualCounts = <String, int>{};
  final screenshots = <Map<String, dynamic>>[];
  late final _TracePainter painter;
  late final List<int> frameIndices;
  int cursor = 0;
  bool endPainted = false, finished = false;
  double? firstDrawMs;
  Map<String, dynamic>? endFrameCpu;
  TraceScene get scene => widget.scene;
  Config get config => widget.config;

  @override
  void initState() {
    super.initState();
    painter = _TracePainter(this);
    frameIndices = List<int>.generate(scene.frames.length, (i) => i);
    SchedulerBinding.instance.addTimingsCallback(onTimings);
  }

  void onTimings(List<ui.FrameTiming> batch) {
    for (final timing in batch) {
      timings.add({
        'frameNumber': timing.frameNumber,
        'buildMs': timing.buildDuration.inMicroseconds / 1000,
        'rasterMs': timing.rasterDuration.inMicroseconds / 1000,
        'totalSpanMs': timing.totalSpan.inMicroseconds / 1000,
        'vsyncStartUs': timing.timestampInMicroseconds(
          ui.FramePhase.vsyncStart,
        ),
        'buildStartUs': timing.timestampInMicroseconds(
          ui.FramePhase.buildStart,
        ),
        'buildFinishUs': timing.timestampInMicroseconds(
          ui.FramePhase.buildFinish,
        ),
        'rasterStartUs': timing.timestampInMicroseconds(
          ui.FramePhase.rasterStart,
        ),
        'rasterFinishUs': timing.timestampInMicroseconds(
          ui.FramePhase.rasterFinish,
        ),
        'rasterFinishWallTimeUs': timing.timestampInMicroseconds(
          ui.FramePhase.rasterFinishWallTime,
        ),
      });
    }
  }

  void paintTrace(Canvas canvas, Size size) {
    if (finished || failing) return;
    final view = View.of(context);
    if (view.physicalSize != const Size(1280, 800) ||
        size != const Size(1280, 800) ||
        view.devicePixelRatio != 1) {
      throw StateError(
        'Require physical client 1280x800 and DPR=1; got physical=${view.physicalSize}, logical=$size, DPR=${view.devicePixelRatio}. Use a 100% scale monitor.',
      );
    }
    final ending = cursor == frameIndices.length;
    final index = ending ? frameIndices.last : frameIndices[cursor];
    final measuring = index >= scene.warmupFrames;
    final phase = ending ? 3 : (measuring ? 2 : 1);
    final seq = ending
        ? scene.measureFrames
        : (measuring ? index - scene.warmupFrames : index);
    final startedMs = startup.elapsedMicroseconds / 1000;
    final totalWatch = Stopwatch()..start();
    final prepareWatch = Stopwatch()..start();
    // All dynamic Path/Paint/Shader/TextPainter objects are created here anew.
    final prepared = scene.prepareFrame(index);
    final nativePrepareMs = prepareWatch.elapsedMicroseconds / 1000;
    final recordWatch = Stopwatch()..start();
    canvas.drawRect(
      const Rect.fromLTWH(0, 0, 1280, 800),
      scene.backgroundPaint,
    );
    final ops = scene.frames[index];
    for (var i = 0; i < prepared.commands.length; i++) {
      prepared.commands[i](canvas);
      if (!ending && measuring) {
        actualCounts.update(
          ops[i][0] as String,
          (n) => n + 1,
          ifAbsent: () => 1,
        );
      }
    }
    // Marker is outside every trace state and is the last recorded drawing.
    drawMarker(canvas, config.tag, phase, seq);
    prepared.dispose();
    final recordSubmitMs = recordWatch.elapsedMicroseconds / 1000;
    final sample = <String, dynamic>{
      'logicalFrame': index,
      'phase': phase,
      'seq': seq,
      'nativePrepareMs': nativePrepareMs,
      'recordSubmitMs': recordSubmitMs,
      'presentMs': null,
      'totalMs': totalWatch.elapsedMicroseconds / 1000,
    };
    firstDrawMs ??= startup.elapsedMicroseconds / 1000;
    if (ending) {
      endFrameCpu = sample;
      if (!endPainted) {
        endPainted = true;
        WidgetsBinding.instance.addPostFrameCallback(
          (_) => unawaited(finish()),
        );
      }
    } else {
      (measuring ? frameCpu : warmupCpu).add(sample);
      producerFrames.add({
        'logicalFrame': index,
        'phase': phase,
        'seq': seq,
        'paintStartSinceEntryMs': startedMs,
      });
      cursor++;
      WidgetsBinding.instance.addPostFrameCallback(
        (_) => unawaited(afterPaint(index)),
      );
    }
  }

  Future<void> afterPaint(int index) async {
    try {
      final seq = index - scene.warmupFrames;
      if (config.verify && {0, 17, 37}.contains(seq)) {
        final boundary =
            boundaryKey.currentContext!.findRenderObject()!
                as RenderRepaintBoundary;
        final image = await boundary.toImage(pixelRatio: 1);
        final bytes = await image.toByteData(format: ui.ImageByteFormat.png);
        final path = '${config.output}.frame$seq.png';
        await File(path).parent.create(recursive: true);
        await File(path).writeAsBytes(
          bytes!.buffer.asUint8List(bytes.offsetInBytes, bytes.lengthInBytes),
        );
        image.dispose();
        screenshots.add({
          'logicalFrame': index,
          'measurementSeq': seq,
          'path': path,
        });
      }
      if (mounted && !endPainted) clock.tick();
    } catch (error, stack) {
      await fail(error, stack);
    }
  }

  Future<void> finish() async {
    try {
      await Future<void>.delayed(const Duration(milliseconds: 750));
      finished = true;
      SchedulerBinding.instance.removeTimingsCallback(onTimings);
      if (!mapEquals(
            normalizeCounts(actualCounts),
            normalizeCounts(scene.expectedCounts),
          ) ||
          frameCpu.length != scene.measureFrames) {
        throw StateError(
          'Executed measurement differs from trace: frames=${frameCpu.length}, counts=$actualCounts, expected=${scene.expectedCounts}',
        );
      }
      if (!mounted) throw StateError('Window unmounted before output');
      final view = View.of(context);
      final result = <String, dynamic>{
        'protocolVersion': 11,
        'framework': 'Flutter',
        'backend': 'Flutter Windows default',
        'status': config.verify ? 'verification' : 'completed',
        'verificationOnly': config.verify,
        'case': scene.data['case'],
        'level': scene.data['level'],
        'seed': scene.data['seed'],
        'traceSha256': scene.traceSha256,
        'trace': config.trace,
        'clientWidth': view.physicalSize.width,
        'clientHeight': view.physicalSize.height,
        'devicePixelRatio': view.devicePixelRatio,
        'tag': config.tag,
        'warmupFrames': scene.warmupFrames,
        'measureFrames': scene.measureFrames,
        ...widget.startupMetrics,
        'firstDrawSinceProcessEntryMs': firstDrawMs,
        'frameCpu': frameCpu,
        'warmupFrameCpu': warmupCpu,
        'producerFrames': producerFrames,
        'actualMeasuredOpcodeTotals': actualCounts,
        'expectedMeasuredOpcodeTotals': scene.expectedCounts,
        'opcodeCountsMatch': mapEquals(
          normalizeCounts(actualCounts),
          normalizeCounts(scene.expectedCounts),
        ),
        'timingEligible': !config.verify,
        'screenshots': screenshots,
        'font': {
          'family': fontFamily,
          'sha256': scene.fontSha256,
          'registration': 'FontLoader.load completed; explicit family used in every TextPainter',
          'fallbackQualification':
              'shared text fixtures must pass nonempty/content/position checks',
        },
        'image': {
          'sha256': scene.imageSha256,
          'width': scene.image.width,
          'height': scene.image.height,
          'filterQuality': scene.imagePaint.filterQuality.name,
          'samplingProfile': scene.data['samplingProfile'],
          'qualification': scene.imagePaint.filterQuality == FilterQuality.none
              ? 'shared integer 1:1 image profile'
              : 'native-scaled linear profile; not fixed quality with software nearest',
        },
        'versions': {
          'flutter': const String.fromEnvironment(
            'CANVAS_FLUTTER_VERSION',
            defaultValue: 'not supplied at build',
          ),
          'engine': const String.fromEnvironment(
            'CANVAS_ENGINE_REVISION',
            defaultValue: 'not supplied at build',
          ),
          'dart': Platform.version,
          'os': Platform.operatingSystemVersion,
          'mode': kReleaseMode
              ? 'release'
              : (kProfileMode ? 'profile' : 'debug'),
        },
        'hardware': {
          'forcedSoftware': false,
          'rendererSelection': 'Flutter Windows default, actual adapter/API not queried; external runner records hardware',
          'rssAtEndBytes': ProcessInfo.currentRss,
          'rssMaxBytes': ProcessInfo.maxRss,
        },
        'diagnostics': {
          'flutterFrameTiming': timings,
          'endFrameCpu': endFrameCpu,
          'frameTimingBoundary': 'Engine callbacks for all reported startup/warmup/measurement/end frames; batches may omit the held final tail. Diagnostic only, not externally visible completion.',
          'cpuBoundary': 'nativePrepare creates per-frame native paths, paints, shaders, layouts and command closures; recordSubmit records every trace operation and marker into CustomPainter Canvas and disposes new text layouts. Canvas recording is not raster/present completion; presentMs is unavailable.',
          'scheduling': 'one next logical frame requested after an actual paint; no wall-clock phase selection or modulo replay',
          'endMarkerHoldMs': 750,
        },
      };
      final output = File(config.output);
      await output.parent.create(recursive: true);
      await output.writeAsString(
        const JsonEncoder.withIndent('  ').convert(result),
      );
      exit(0);
    } catch (error, stack) {
      await fail(error, stack);
    }
  }

  @override
  Widget build(BuildContext context) => RepaintBoundary(
    key: boundaryKey,
    child: CustomPaint(
      painter: painter,
      willChange: true,
      child: const SizedBox.expand(),
    ),
  );
  @override
  void dispose() {
    SchedulerBinding.instance.removeTimingsCallback(onTimings);
    clock.dispose();
    super.dispose();
  }
}
