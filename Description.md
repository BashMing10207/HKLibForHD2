# HKLibForHD2 - Helldivers 2 Havok Toolchain Documentation

## 1. 프로젝트 목표

이 프로젝트의 최종 목표는 Helldivers 2 게임 에셋에 사용된 Havok 2019 (`hk2019`) 포맷의 바이너리 파일(`.hkx`)을 XML 포맷으로 변환(Unpack)하고, 다시 바이너리 포맷으로 재변환(Repack)할 수 있는 C# 기반의 툴체인을 구축하는 것입니다. 이를 통해 모델의 본(bone) 구조를 수정하는 등의 모딩 작업을 가능하게 합니다.

## 2. 핵심 문제: 마스터 스키마 의존성

개발 초기 단계에서 `.physics.hkx`와 같은 파일을 파싱할 때 `System.IO.InvalidDataException: Invalid type index ...` 오류가 지속적으로 발생했습니다.

### 원인

이 문제의 근본 원인은 Helldivers 2의 `.hkx` 파일들이 독립적으로 완전한 파일이 아니라는 점에 있습니다. 대부분의 에셋 파일은 파일 크기를 줄이기 위해 전체 타입 정보를 포함하지 않고, 대신 게임 엔진이 로드하는 **마스터 스키마(Master Schema)** 파일에 정의된 타입의 **인덱스(해시)**만을 참조합니다.

- **마스터 스키마 파일:** `global.havok_physics_properties.main`
- **오류 현상:**
  1. `.hkx` 파일 자체에는 95개의 타입만 정의되어 있습니다 (`Max is 94`).
  2. 하지만 실제 데이터는 12296번과 같이 파일 내에 존재하지 않는 타입을 참조합니다.
  3. 독립형 파서는 이 타입을 찾을 수 없어 `Invalid type index` 오류를 발생시킵니다.

## 3. 해결 아키텍처

이 문제를 해결하기 위해, 게임 엔진과 유사하게 마스터 스키마를 먼저 읽어들여 타입 정보를 완전히 구축한 후, 대상 파일을 파싱하는 2단계 아키텍처를 구현했습니다.

### 3.1. 동적 타입 레지스트리 (`DynamicTypeRegistry.cs`)

이 클래스는 게임 엔진의 "타입 사전" 역할을 수행합니다.

1.  **기본 스키마 로드:** 먼저 `HavokTypeRegistry20190100.xml` 파일을 읽어 Havok의 모든 기본 타입(`hkVector4`, `hkpRigidBody` 등) 정보를 로드합니다.
2.  **게임 전용 스키마 파싱:** 그 다음, `global.havok_physics_properties.main` 파일을 바이너리 모드로 읽어 Helldivers 2 고유의 타입 정보를 파싱하고 기본 스키마에 병합합니다.
    - 이 과정은 `TYPE` 섹션 내의 `STR1`(문자열 테이블), `TST1`(타입 정의), `FST1`(필드 정의), `THSH`(타입 해시) 하위 섹션을 순차적으로 파싱하여 이루어집니다.

### 3.2. hk2019 역직렬화 엔진 (`HavokBinarySerializer.cs`)

실제 `.hkx` 에셋 파일을 읽는 핵심 엔진입니다.

1.  **컴펜디움 로드:** 파일 처리 전, `LoadCompendium()` 메서드를 통해 `DynamicTypeRegistry`가 마스터 스키마를 로드하도록 합니다.
2.  **`TAG0` 헤더 파싱:** 파일의 `TAG0` 헤더를 찾아 `__data__` 섹션의 위치를 식별합니다. 게임 파일들은 `TAG0` 앞에 추가 데이터가 있을 수 있으므로, 매직 코드를 스캔하여 오프셋을 찾는 기능이 포함되어 있습니다.
3.  **타입 해시 기반 역직렬화:** `__data__` 섹션의 객체를 읽을 때, `hk2018` 방식의 4바이트 로컬 인덱스 대신 **8바이트 타입 해시(Type Hash)**를 읽습니다.
4.  **타입 조회:** 읽어온 해시 값을 `DynamicTypeRegistry`에 조회하여 해당 객체의 정확한 타입 정보(`DynamicHavokType`)를 가져옵니다.
5.  **데이터 파싱:** 조회된 타입 정보를 바탕으로 객체의 크기, 필드 등을 올바르게 해석합니다. (현재는 타입 식별 후 건너뛰기까지만 구현됨)

## 4. 주요 오류 및 해결 과정

### 4.1. `Invalid type index`

- **원인:** `hk2018`의 4바이트 인덱스 기반 파싱 로직이 `hk2019`의 8바이트 해시 기반 데이터에 잘못 적용됨.
- **해결:** `HavokBinarySerializer`의 `Read` 메서드와 그 하위 호출을 `DynamicTypeRegistry`와 8바이트 해시를 사용하도록 전면 재구현.

### 4.2. `Could not find the __types__ section`

- **원인:** 컴펜디움 파일(`global.havok_physics_properties.main`) 역시 `TAG0` 헤더 앞에 추가 데이터가 있어, 파일의 처음부터 읽으려던 `LoadCompendium` 메서드가 실패함.
- **해결:** `ReadTAG0` 메서드에 파일 스트림을 스캔하여 `TAG0` 매직 코드의 실제 시작 위치를 찾는 로직을 추가. 또한, `TAG0` 헤더가 없더라도 `TYPE` 매직 코드를 직접 스캔하는 폴백(fallback) 로직을 추가하여 안정성을 높임.

### 4.3. `EndOfStreamException`

- **원인:** `DynamicTypeRegistry.ParseFromBinary` 메서드가 컴펜디움 파일의 `TYPE` 섹션 구조를 잘못 가정함. (헤더에 전체 크기나 하위 섹션 테이블이 있을 것으로 예상)
- **해결:** `TYPE` 섹션의 헤더를 읽으려는 로직을 제거하고, 대신 `TST1`, `FST1` 등 하위 섹션들의 매직 코드를 순차적으로 스캔하고 각 섹션의 헤더를 개별적으로 파싱하도록 수정.

## 5. 현재 상태 및 다음 단계

- **현재 상태:** `Phase 2.5`까지 완료. 마스터 스키마를 이용한 `hk2019` 파일의 타입 식별 및 기본 구조 파싱이 가능해짐. `Invalid type index` 오류 해결.
- **다음 단계:** `Phase 4: 무결성 검증`
  - `ReadObjectData` 메서드에 실제 객체 데이터를 재귀적으로 파싱하여 C# 객체로 변환하는 로직을 구현해야 합니다.
  - 이후, 수정된 C# 객체를 다시 바이너리 포맷으로 직렬화(Serialization)하는 `Write` 관련 로직을 완성해야 합니다.

---
*이 문서는 Gemini Code Assist와의 대화형 개발 세션을 통해 작성되었습니다.*