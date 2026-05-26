# HKLib for Helldivers 2

Helldivers 2 모딩을 위한 C# 라이브러리로, Havok 2019 바이너리 태그파일(`.hkx`)의 읽기/쓰기를 지원합니다. 이 툴체인은 Havok의 바이너리 포맷을 사람이 읽을 수 있는 XML 포맷으로 변환하고, 다시 바이너리로 패킹하여 게임 데이터의 분석 및 수정을 가능하게 합니다.

## 핵심 기능

*   **완벽한 Havok 2019 지원**: `hk2019` 바이너리 표준의 읽기/쓰기를 모두 구현합니다.
*   **견고한 직렬화 (쓰기) 엔진**:
    *   **2-Pass 쓰기 알고리즘**: 구조 수정(예: 스켈레톤에 본 추가) 시에도 포인터 주소를 정확하게 재배치하기 위해, 쓰기 전 객체 레이아웃과 오프셋을 미리 계산합니다.
    *   **포인터 재배치 (`__patch__`)**: 게임 엔진이 메모리 주소를 올바르게 참조할 수 있도록, 파일의 `__patch__` 섹션을 자동으로 생성합니다.
    *   **16바이트 정렬**: `hk2019` 포맷이 특정 데이터 구조에 요구하는 엄격한 16바이트 메모리 정렬 규칙을 준수합니다.
    *   **완전한 메타데이터 생성**: `__classnames__`, `__types__` (클래스 해시 `8THSH` 포함), 최종 `TAG0` 헤더/푸터 등 파일 무결성과 호환성에 필수적인 모든 메타데이터 섹션을 생성합니다.
*   **고급 역직렬화 (읽기) 엔진**:
    *   **재귀적 객체 그래프 파싱**: 복잡하게 중첩된 Havok 파일의 구조를 C# 객체 그래프로 완벽하게 디코딩합니다.
    *   **컴펜디움/마스터 스키마 지원**: 개별 에셋 파일에 포함되지 않은 타입 정의를 해석하기 위해 외부 "컴펜디움" 파일(예: `global.havok_physics_properties.main`)을 로드할 수 있습니다. 이는 Helldivers 2 에셋 처리에 필수적인 기능입니다.
    -- 이 기능은 있을 수도 있고 없을 수도 있기에, 예외처리로서 작동하게 해주세요.
*   **커맨드 라인 인터페이스 (CLI)**:
    *   **Unpack**: 바이너리 `.hkx` 파일을 수정 가능한 XML 포맷으로 변환합니다.
    *   **Pack**: 수정된 XML 파일을 게임에서 사용 가능한 바이너리 `.hkx` 포맷으로 다시 변환합니다.

## 아키텍처 개요

이 라이브러리는 Havok 바이너리 포맷을 위한 독립적인 컴파일러처럼 작동하도록 설계되었습니다. 핵심 목표는 수정 후에도 바이너리 무결성을 보장하는 것입니다.

*   **`DynamicTypeRegistry`**: 타입 시스템의 핵심입니다. 베이스 Havok 2019 타입 정의(`HavokTypeRegistry20190100.xml`)와 컴펜디움 파일에서 발견된 타입을 함께 로드하여 관리합니다. 이를 통해 직렬화기는 마주치는 모든 Havok 클래스의 구조를 이해할 수 있습니다.
*   **`HavokBinarySerializer`**: 직렬화를 위한 고수준 인터페이스입니다.
    *   **읽기**: 파일의 `TAG0` 헤더를 파싱하고 데이터 섹션을 식별한 후, 재귀적 파서(`ReadObject`)를 사용해 바이너리 데이터로부터 C# 객체 그래프를 빌드합니다.
    *   **쓰기**: `HavokBinaryWriter`가 처리하는 2-Pass 알고리즘을 개시합니다.
*   **`HavokBinaryWriter`**: 직렬화를 위한 저수준의 핵심 엔진입니다.
    *   **Pass 1 (`Pass1_DiscoverAndLayout`):** 쓰여질 전체 객체 그래프를 순회하며 모든 객체와 데이터 블록의 크기 및 정렬을 계산하고, `__data__` 섹션의 가상 메모리 맵을 생성합니다.
    *   **Pass 2 (`Pass2_WriteData` & `WritePatchSection`):** Pass 1에서 계산된 레이아웃에 따라 실제 데이터를 씁니다. 이 과정에서 모든 포인터의 위치를 기록하고, 최종적으로 이 기록을 사용해 엔진이 로드 타임에 포인터를 올바르게 수정할 수 있도록 `__patch__` 섹션을 생성합니다.

## 사용법 (CLI 도구)

`HKLib.CLI.exe`는 파일 변환을 위한 간단한 커맨드 라인 인터페이스를 제공합니다.

*   **Unpacking (`.hkx` -> `.xml`)**
    바이너리 `.hkx` 파일을 XML로 변환하려면, 실행 파일에 파일 경로를 인자로 전달하십시오.
    ```
    HKLib.CLI.exe "C:\path\to\your\file.hkx"
    ```
    이 도구는 필요한 컴펜디움 파일(예: `global.havok_physics_properties.main`)을 `.hkx` 파일과 동일한 디렉토리 및 실행 파일 디렉토리에서 자동으로 탐색합니다.
    수동으로 컴펜디움 파일을 지정할 수도 있습니다:
    ```
    HKLib.CLI.exe "C:\path\to\your\file.hkx" --compendium:"C:\path\to\compendium.main"
    ```

*   **Packing (`.xml` -> `.hkx`)**
    XML 파일을 바이너리 `.hkx` 파일로 다시 변환하려면:
    ```
    HKLib.CLI.exe "C:\path\to\your\file.xml"
    ```
    패킹 시에도 컴펜디움 파일이 필요하며, 언패킹과 동일한 자동 탐색 로직을 사용합니다.

## 개발자 및 유지보수를 위한 가이드

이 프로젝트는 `hk2019` 호환 직렬화기를 체계적으로 구축하기 위해 단계적 접근법으로 개발되었습니다. `TODOLIST.md` 파일에 그 과정이 기록되어 있습니다.

*   **핵심 로직**: 주된 직렬화/역직렬화 로직은 `HKLib.Serialization.hk2019.Binary.HavokBinarySerializer.cs`와 `HKLib.Serialization.Binary.HavokBinaryWriter.cs`에 있습니다. 이 두 파일을 이해하는 것이 라이브러리를 유지보수하거나 확장하는 데 핵심적입니다.
*   **타입 시스템**: Havok 클래스의 C# 표현(예: `hkaSkeleton`, `hkbBone`)은 `HKLib/hk/Autogen` 디렉토리에 위치합니다. 이들은 공식 `HavokTypeRegistry20190100.xml` 스키마로부터 생성되었습니다. `HKLib.Reflection`의 `DynamicTypeRegistry`가 런타임에 이 타입들을 관리합니다.
*   **향후 작업 및 검증**: 다음 핵심 단계는 포괄적인 **Round-trip Test** (Phase 4.2)를 구현하고 통과하는 것입니다. 이는 `.hkx` 파일을 읽고, 변경 없이 다시 쓴 후, 결과물이 원본과 바이트 단위로 동일한지 검증하는 과정입니다. 이 테스트는 직렬화기의 정확성과 무결성을 증명하는 최종적인 척도가 될 것입니다.

# Special Thanks
* [Skyth](https://github.com/blueskythlikesclouds) - Reverse engineered Havok 2016 tagfiles and created TagTools
* [GoogleBen](https://github.com/googleben) - Reverse engineered Havok 2018 tagfiles
* [Katalash](https://github.com/katalash) - Created HKX2 which inspired HKLib
* [TKGP](https://github.com/JKAnderson) - For his BinaryWriter/Reader implementations
