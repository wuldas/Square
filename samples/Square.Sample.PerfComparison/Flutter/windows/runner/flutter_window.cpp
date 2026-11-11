#include "flutter_window.h"

#include <flutter/standard_method_codec.h>
#include <windows.h>
#include <psapi.h>
#include <chrono>

#include <optional>

#include "flutter/generated_plugin_registrant.h"

namespace {

// Handles the "square.perf/bench" MethodChannel used by lib/main.dart for the
// shared benchmark contract:
//   memoryInfo -> {privateBytes: private commit (PrivateUsage),
//                  workingSetBytes: working set} in bytes, GPU VRAM excluded
//   cpuTimes   -> {kernelSeconds, userSeconds} process CPU time totals
void HandleBenchCall(
    const flutter::MethodCall<flutter::EncodableValue>& call,
    std::unique_ptr<flutter::MethodResult<flutter::EncodableValue>> result) {
  if (call.method_name() == "memoryInfo") {
    PROCESS_MEMORY_COUNTERS_EX info{};
    info.cb = sizeof(info);
    if (!::GetProcessMemoryInfo(
            ::GetCurrentProcess(),
            reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&info), sizeof(info))) {
      result->Error("memoryInfo_failed", "GetProcessMemoryInfo failed");
      return;
    }
    flutter::EncodableMap response{
        {flutter::EncodableValue("privateBytes"),
         flutter::EncodableValue(static_cast<int64_t>(info.PrivateUsage))},
        {flutter::EncodableValue("workingSetBytes"),
         flutter::EncodableValue(static_cast<int64_t>(info.WorkingSetSize))},
    };
    result->Success(flutter::EncodableValue(std::move(response)));
    return;
  }
  if (call.method_name() == "cpuTimes") {
    FILETIME creation{};
    FILETIME exit_time{};
    FILETIME kernel_time{};
    FILETIME user_time{};
    if (!::GetProcessTimes(::GetCurrentProcess(), &creation, &exit_time,
                           &kernel_time, &user_time)) {
      result->Error("cpuTimes_failed", "GetProcessTimes failed");
      return;
    }
    auto filetime_seconds = [](const FILETIME& filetime) {
      ULARGE_INTEGER value;
      value.LowPart = filetime.dwLowDateTime;
      value.HighPart = filetime.dwHighDateTime;
      // FILETIME resolution is 100 ns.
      return static_cast<double>(value.QuadPart) * 1e-7;
    };
    flutter::EncodableMap response{
        {flutter::EncodableValue("kernelSeconds"),
         flutter::EncodableValue(filetime_seconds(kernel_time))},
        {flutter::EncodableValue("userSeconds"),
         flutter::EncodableValue(filetime_seconds(user_time))},
        {flutter::EncodableValue("sampledWallMicros"),
         flutter::EncodableValue(static_cast<int64_t>(
             std::chrono::duration_cast<std::chrono::microseconds>(
                 std::chrono::system_clock::now().time_since_epoch())
                 .count()))},
    };
    result->Success(flutter::EncodableValue(std::move(response)));
    return;
  }
  result->NotImplemented();
}

}  // namespace

FlutterWindow::FlutterWindow(const flutter::DartProject& project)
    : project_(project) {}

FlutterWindow::~FlutterWindow() {}

bool FlutterWindow::OnCreate() {
  if (!Win32Window::OnCreate()) {
    return false;
  }

  RECT frame = GetClientArea();

  // The size here must match the window dimensions to avoid unnecessary surface
  // creation / destruction in the startup path.
  flutter_controller_ = std::make_unique<flutter::FlutterViewController>(
      frame.right - frame.left, frame.bottom - frame.top, project_);
  // Ensure that basic setup of the controller was successful.
  if (!flutter_controller_->engine() || !flutter_controller_->view()) {
    return false;
  }
  RegisterPlugins(flutter_controller_->engine());
  SetChildContent(flutter_controller_->view()->GetNativeWindow());

  bench_channel_ =
      std::make_unique<flutter::MethodChannel<flutter::EncodableValue>>(
          flutter_controller_->engine()->messenger(), "square.perf/bench",
          &flutter::StandardMethodCodec::GetInstance());
  bench_channel_->SetMethodCallHandler(
      [](const flutter::MethodCall<flutter::EncodableValue>& call,
         std::unique_ptr<flutter::MethodResult<flutter::EncodableValue>>
             result) { HandleBenchCall(call, std::move(result)); });

  flutter_controller_->engine()->SetNextFrameCallback([&]() {
    this->Show();
  });

  // Flutter can complete the first frame before the "show window" callback is
  // registered. The following call ensures a frame is pending to ensure the
  // window is shown. It is a no-op if the first frame hasn't completed yet.
  flutter_controller_->ForceRedraw();

  return true;
}

void FlutterWindow::OnDestroy() {
  // The channel borrows the engine messenger, so reset it first.
  bench_channel_ = nullptr;
  if (flutter_controller_) {
    flutter_controller_ = nullptr;
  }

  Win32Window::OnDestroy();
}

LRESULT
FlutterWindow::MessageHandler(HWND hwnd, UINT const message,
                              WPARAM const wparam,
                              LPARAM const lparam) noexcept {
  // Give Flutter, including plugins, an opportunity to handle window messages.
  if (flutter_controller_) {
    std::optional<LRESULT> result =
        flutter_controller_->HandleTopLevelWindowProc(hwnd, message, wparam,
                                                      lparam);
    if (result) {
      return *result;
    }
  }

  switch (message) {
    case WM_FONTCHANGE:
      flutter_controller_->engine()->ReloadSystemFonts();
      break;
  }

  return Win32Window::MessageHandler(hwnd, message, wparam, lparam);
}
