cmake_minimum_required(VERSION 3.20)
project(FHGOriginalGuard LANGUAGES CXX)
if(NOT WIN32)
  message(FATAL_ERROR "Windows only")
endif()

# Keep filters for prior FHG v0.2/v0.3 installations UNCHANGED.
# Both WFP provider and sublayer identities for v0.4 differ from the previous
# controller. Reinstalling v0.4 cannot delete filters owned by earlier versions.
file(READ "${CMAKE_CURRENT_SOURCE_DIR}/../guard/FHGGuard.cpp" SOURCE)
string(REPLACE "0xad104a20" "0xad104a40" SOURCE "${SOURCE}")
string(REPLACE "0xec768632" "0xec768652" SOURCE "${SOURCE}")
string(REPLACE "FHG isolated Firefox egress guard" "FHG original Hiddify UI Firefox guard" SOURCE "${SOURCE}")
file(WRITE "${CMAKE_CURRENT_BINARY_DIR}/FHGGuardOriginal.cpp" "${SOURCE}")

add_executable(FHGGuard "${CMAKE_CURRENT_BINARY_DIR}/FHGGuardOriginal.cpp")
target_compile_features(FHGGuard PRIVATE cxx_std_17)
target_compile_definitions(FHGGuard PRIVATE UNICODE _UNICODE NOMINMAX)
target_link_libraries(FHGGuard PRIVATE Fwpuclnt Advapi32)
if(MSVC)
  target_compile_options(FHGGuard PRIVATE /W4 /EHsc)
endif()

add_executable(FHGProbe EXCLUDE_FROM_ALL ../guard/NetworkProbe.cpp)
target_compile_features(FHGProbe PRIVATE cxx_std_17)
target_link_libraries(FHGProbe PRIVATE Ws2_32)
