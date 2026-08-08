cmake_minimum_required(VERSION 3.21)
project(navihmi-fw VERSION 0.1.0 LANGUAGES CXX)

# ═══════════════════════════════════════════════════════════
# NavigatorHMI FW 端 Qt Application（跨平台）
# 同一份代码：Windows 桌面版（仿真器/预演）+ Linux 嵌入式版（i.MX6ULL）
# 输入：组态软件编译的 .navihmi（fw/proto/navihmi.proto 契约）
# ═══════════════════════════════════════════════════════════

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)
set(CMAKE_AUTOUIC ON)

find_package(Qt6 6.5 REQUIRED COMPONENTS Core Gui Qml Quick Widgets)
find_package(Protobuf REQUIRED)

# 平台后端：Windows 桌面 = 仿真器；Linux 嵌入式 = linuxfb（Buildroot/rootfs 提供 EGL 时可用 eglfs）
if(WIN32)
    set(PLATFORM_BACKEND "windows")
else()
    # 嵌入式默认 linuxfb；如需硬件加速可在构建时 -DPLATFORM_BACKEND=eglfs 覆盖
    if(NOT DEFINED PLATFORM_BACKEND)
        set(PLATFORM_BACKEND "linuxfb")
    endif()
endif()

qt_add_executable(navihmi-fw
    src/main.cpp
    src/projectparser.h
    src/projectparser.cpp
    src/runtimemodel.h
    src/runtimemodel.cpp
    src/tagstore.h
    src/tagstore.cpp
    src/alarmengine.h
    src/alarmengine.cpp
    src/hmiview.h
    src/hmiview.cpp
)

# protobuf 生成（navihmi.proto → C++ 解析类）
set(PROTO_SRC "${CMAKE_SOURCE_DIR}/../proto/navihmi.proto")
protobuf_generate_cpp(PROTO_SRCS PROTO_HDRS "${PROTO_SRC}")

target_sources(navihmi-fw PRIVATE ${PROTO_SRCS} ${PROTO_HDRS})
target_include_directories(navihmi-fw PRIVATE ${CMAKE_CURRENT_BINARY_DIR} ${Protobuf_INCLUDE_DIRS})

qt_add_qml_module(navihmi-fw
    URI Navihmi
    VERSION 1.0
    QML_FILES
        qml/MainScreen.qml
        qml/NavBar.qml
        qml/AlarmBanner.qml
    SOURCES
        src/hmiview.h
        src/hmiview.cpp
)

target_link_libraries(navihmi-fw PRIVATE
    Qt6::Core Qt6::Gui Qt6::Qml Qt6::Quick Qt6::Widgets
    protobuf::libprotobuf
)

# 运行参数（可被 -D 覆盖）
set(FW_DEVICE_WIDTH "800" CACHE STRING "设备宽度")
set(FW_DEVICE_HEIGHT "480" CACHE STRING "设备高度")

# Linux 嵌入式：链接屏显相关库
if(NOT WIN32)
    target_link_libraries(navihmi-fw PRIVATE ${CMAKE_DL_LIBS})
    target_compile_definitions(navihmi-fw PRIVATE PLATFORM_BACKEND="${PLATFORM_BACKEND}")
endif()
