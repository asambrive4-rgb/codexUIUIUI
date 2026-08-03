---
name: reduce-ui-jank
description: >
  WPF 앱 UI 렉·스터터링·프레임 드랍·창/팝업 전환 지연을 분석 → 기획(3안) → 구현 →
  Windows/WPF 측정으로 검증하는 루프. Dispatcher(UI 스레드) 부하, 불필요한
  바인딩 재평가·레이아웃 패스, 동기 I/O·프로세스 제어, 사용량 폴링·스냅샷
  갱신 병목을 찾아 우선순위를 매기고, 항상 보완 깊이별 3가지 방안을 제시한 뒤
  사용자가 고른 방향으로 고치고 before/after로 효과를 숫자·체감으로 확인한다.
  Use when: "UI 렉", "스터터링", "프레임 드랍", "jank", "버벅", "끊김",
  "메인 스레드", "Dispatcher", "부드럽지 않", "성능 병목", "창이 느림",
  "팝업 느림", "트레이 느림", "사용량 갱신 렉", "reduce-ui-jank",
  "/reduce-ui-jank",
  또는 WPF 화면 동작이 무거워 분석·기획·측정으로 개선하고 싶을 때.
metadata:
  short-description: "WPF UI jank: analyze → 3 options → measure"
---

# /reduce-ui-jank — WPF UI 렉·스터터링 줄이기

**Windows WPF 데스크톱 앱**(이 레포: Codex Account Switcher / `codexUIUIUI`)에서
끊김·버벅임·프레임 드랍·창/팝업 전환 지연을
**코드 분석 → 방안 제시 → 구현 → 측정 검증** 순으로 다룬다.

이 skill은 “무조건 최소 변경”을 강요하지 않는다.  
대신 **깊이 다른 3가지 보완 방안**을 항상 제시하고, 사용자가 고른 안으로 진행한다.

**하지 말 것:** Android `adb` / `dumpsys gfxinfo` / Compose / Gradle / Wear 전용 절차.  
이 프로젝트는 WPF + .NET 이다.

**DeckDeckDeck skill과의 차이(이 레포):** 썸네일·홈 네비·핫키 팔레트가 아니라
**트레이·프로필 목록·팝업·Codex 실행/전환·사용량(rate limit) 폴링**이 중심이다.
이미지 디코드 파이프라인·`StartupTimingLog`·네비 캐시 타이밍 테스트는 **없다**.

---

## 언제 쓰는가

- 트레이에서 메인 창/팝업을 다시 열 때 멈칫
- 프로필 목록 표시·스크롤·상태 뱃지 갱신이 끊김
- 실행/전환/삭제 직후 UI가 한동안 굳음
- 사용량(한도) 자동 갱신 중에 입력·전환이 버벅임
- 새 프로필 로그인 창·확인 창 표시가 무거움
- 사용자가 `/reduce-ui-jank` 또는 위 키워드로 요청

---

## 전체 루프 (필수 순서)

```
1) 분석(Analysis)     → 병목 후보 + 우선순위 보고서 (+ 가능하면 before 실측)
2) 기획(Plan)         → 반드시 3안 제시 → 사용자 선택
3) 구현(Implement)    → 선택한 안만 (또는 합의된 범위)
4) 검증(Verify)       → 회귀 테스트 + 실측(L1~L4) + 이전 기록과 비교
5) 보고(Report)       → 계층별 숫자·체감 비교, 남은 리스크, 다음 후보
```

중간에 사용자가 “분석만 / 기획만 / 측정만”이라고 하면 해당 단계만 한다.

프로젝트 지침:

- `AGENTS.md` — 한국어 설명, 최소 관련 변경, 테스트·루트 exe 준비 규칙
- `docs/clean-architecture.mini.md` — UI / Core(유스케이스·도메인) / Infrastructure 책임 경계
- 제품 맥락이 필요하면 `docs/PRODUCT_MVP.md`, 알려진 한계는 `docs/KNOWN_LIMITATIONS.md`

이 레포에는 `docs/DESIGN_GUIDELINES.md` 와
`docs/a-philosophy-of-software-design.mini.md` 가 **없다**.  
디자인 토큰 문서를 전제로 한 검사 단계는 생략하고, 기존 XAML/스타일 패턴을 유지한다.

---

## Phase 1 — 분석

### 1.1 범위 잡기

- 화면·시나리오를 **한 문장**으로 고정한다.  
  예: “트레이 더블클릭 후 메인 창이 보이고 입력이 될 때까지”  
  예: “프로필 팝업이 뜬 상태에서 사용량 숫자가 갱신될 때”  
  예: “활성 프로필 전환 버튼 클릭 후 UI가 다시 반응할 때까지”
- 애매하면 짧게 확인한다. 합리적 기본값이 있으면 밝히고 진행한다.

CodexSwitcher에서 자주 쓰는 시나리오 후보:

| ID | 시나리오 |
|----|----------|
| S-start | 앱 기동 후 트레이 준비·첫 표면 표시까지 |
| S-tray | 트레이에서 메인 창/기본 팝업 재표시 |
| S-popup | 프로필 팝업 열기·위치 복원·닫기 |
| S-list | 프로필 목록 첫 로드·상태(런타임) 갱신 표시 |
| S-usage | 사용량 모니터 주기 갱신 / 수동 새로고침 |
| S-run | 프로필 실행 (Codex 기동 경로) |
| S-switch | 프로필 전환 (종료·재실행 포함) |
| S-login | 새 프로필 생성·로그인 창 흐름 |
| S-delete | 삭제 확인 창 → 삭제 완료 후 목록 |

### 1.2 코드에서 찾을 것 (WPF 우선순위 단서)

| 증상 후보 | 코드 신호 |
|-----------|-----------|
| 주기적 hitch | `PeriodicTimer`, 짧은 주기의 `Dispatcher.InvokeAsync`, 타이머마다 큰 VM 갱신 |
| 전환/실행 직후 멈칫 | UI 스레드 동기 대기, 프로세스 제어·파일 I/O가 디스패처를 막음 |
| 목록이 늦게 채워짐 | 컬렉션 전체 재구성, 다중 `OnPropertyChanged` 연쇄, 스냅샷 일괄 적용 |
| 창/팝업 표시 느림 | 생성자·`Loaded` 무거운 초기화, 트레이→Show 경로의 동기 작업 |
| 사용량 갱신 중 버벅임 | `ProfileUsageMonitor` 이벤트 → UI 마샬링 폭주, 프로필 수 × 프로세스 probe |
| 레이아웃 thrash | 잦은 크기/가시성 변경, 팝업 placement 재측정, 과도한 템플릿 재생성 |

**검색 키워드 예 (이 레포 기준):**

```text
Dispatcher.Invoke
Dispatcher.BeginInvoke
Dispatcher.InvokeAsync
PeriodicTimer
Task.Run
OnPropertyChanged
SetProperty
ProfileUsageMonitor
UsageMonitorDiagnostics
RefreshAllNowAsync
RefreshRuntimeState
RuntimeMonitorInterval
AcquireVisibleSurface
PauseAsync
WindowsCodexRateLimitReader
WindowsProfileStore
TrayIconService
ProfilePopupWindow
PopupPlacementStore
ShowDefaultSurface
File.Read
File.Exists
Process
```

**이미 잘 된 부분(분석 시 “유지”로 적을 것, 함부로 제거 금지):**

- `ProfileUsageMonitor` — 화면이 보일 때만 `AcquireVisibleSurface`로 폴링 시작, 작업 중 `PauseAsync`
- `MainWindow` — 사용량 스냅샷을 모아 `DispatcherPriority.Background`로 드레인 (폭주 완화)
- `ProfileUsageRefreshPolicy` — 활성/비활성·실패 백오프 간격 분리
- `UsageMonitorDiagnostics` — 환경 변수로만 켜지는 진단 로그 (상시 비용 없음)
- 유스케이스·스토어가 Core/Infrastructure에 분리되어 있음 (UI에 규칙 끌어오지 말 것)

**책임 경계 주의 (이 레포 구조):**

| 계층 | 경로 | jank 작업에서 |
|------|------|----------------|
| UI / Presentation | `src/CodexSwitcher.Bootstrapper` | 디스패처, 바인딩, 창 수명, 표시용 스로틀·배치 |
| Core | `src/CodexSwitcher.Core` | 유스케이스·도메인 규칙 — Bitmap/UI 캐시 타입 금지 |
| Infrastructure | `src/CodexSwitcher.Infrastructure` | 파일·프로세스·Codex CLI 읽기 — 가능하면 비동기 유지 |

- jank 원인이 도메인 “규칙”이 아니라 **표시/폴링/I/O 배치**인 경우가 많다.
- 규칙을 UI에 끌어오거나, Infrastructure를 ViewModel에 직접 때려 넣지 않는다.
- 표시용 캐시·스로틀은 Bootstrapper 쪽에 두고, Core를 오염시키지 않는다.

### 1.3 실측 (가능하면 분석 단계부터)

구현 **전 before** 를 잡는다. 세부 절차는  
`references/measure-wpf-performance.md` 를 따른다.

기록할 것 (가능한 범위):

| 지표 | 의미 |
|------|------|
| 시나리오 완료 시간 (ms) | 스톱워치 / `Stopwatch` / (합의된) 임시 계측 |
| UI 스레드 블로킹 | VS Diagnostic Tools, 수동 “입력이 안 먹힌 구간” |
| 레이아웃/렌더 비용 | WPF Performance / Timeline 샘플 |
| 사용량 폴링 부담 | 프로필 수, 갱신 주기, UI 이벤트 빈도 |
| 체감 메모 | 끊김 횟수, 어느 동작 직후인지 |

**L1 참고:** 이 앱에는 DeckDeckDeck 식 `StartupTimingLog` / 상시 `app.log` 기동 구간이 **없다**.  
기동·구간 실측이 필요하면 (1) 스톱워치 L4, (2) 합의된 임시 `Stopwatch` 로그,  
(3) `CODEX_SWITCHER_USAGE_DIAGNOSTICS=1` 일 때의  
`%LOCALAPPDATA%\CodexAccountSwitcher\usage-monitor.log` (사용량 시나리오 한정) 을 쓴다.

측정 불가 시: **이유를 쓰고**, 코드 기반 추정임을 명시한다.  
“느릴 것 같다”만으로 Phase 3으로 가지 않는다.

### 1.4 분석 산출물

사용자에게 **한국어**, 쉬운 말로:

1. 한 줄 요약  
2. 이미 잘 된 부분  
3. 병목 목록 (P0 / P1 / P2…) + 파일·근거  
4. 시나리오별 부담 지도  
5. (있으면) before 숫자  
6. 다음 단계: 기획 3안 제시 예정  

보고서 파일은 사용자가 요청할 때만 저장한다. 기본은 대화 응답.

---

## Phase 2 — 기획 (3안 필수)

**구현 전에** 반드시 아래 3단 깊이로 방안을 제시한다.  
“최소 변경만” 고르지 말고, 트레이드오프를 드러낸다.

### 안 A — 기존 방법에서 보완

- 구조·API·책임 경계는 거의 유지
- 폴링 간격·백오프, 스냅샷 배치/스로틀, 불필요 `PropertyChanged` 제거,  
  `DispatcherPriority` 조정, 동기 경로 가드, 불필요 재조회 스킵 등
- **장점:** 리스크·리뷰 범위 작음  
- **단점:** 한계가 빨리 올 수 있음  
- **예상 효과 / 검증 방법**

### 안 B — 기존 로직·아키텍처를 조금 수정하며 보완

- 무거운 작업을 UI 밖·백그라운드로 이동, 상태 위치 조정,  
  팝업/메인 표시 시 로드 순서 변경, 표시 전용 모델 정리, 디스패처 사용 정리
- **장점:** 원인에 더 직접적  
- **단점:** 호출부·테스트·바인딩 수정 필요  
- **예상 효과 / 검증 방법**

### 안 C — 로직·아키텍처·패러다임을 더 크게 바꾸며 보완

- 사용량 파이프라인 재설계, 창/팝업 수명주기 재구성,  
  런타임·사용량 모니터 통합, 가상화 목록, 프로세스 probe 캐시 계층 등
- **장점:** 천장 성능·유지보수에 유리할 수 있음  
- **단점:** 범위·회귀 위험 큼, 단계 분할 필요할 수 있음  
- **예상 효과 / 검증 방법**

### 기획 표 형식 (권장)

| | 안 A (보완) | 안 B (소수정) | 안 C (대수정) |
|--|-------------|---------------|---------------|
| 핵심 아이디어 | | | |
| 주요 터치 파일 | | | |
| AGENTS/아키텍처 충돌 | | | |
| 리스크 | | | |
| 예상 체감 | | | |
| 권장 여부 | (한 줄 이유) | | |

- 분석 근거상 **추천 1개**를 표시하되, 선택을 강요하지 않는다.
- 사용자가 고르기 전 **코드를 크게 바꾸지 않는다**.
- 기존 UI 패턴·WPF-UI(`FluentWindow`) 스타일을 불필요하게 바꾸지 않는다.

---

## Phase 3 — 구현

1. 사용자가 고른 안(또는 “A+B 일부” 등 합의 범위)만 구현한다.  
2. 고르지 않은 안의 대규모 리팩터를 몰래 섞지 않는다.  
3. 사용자 대면 설명은 한국어·쉬운 말로 (`AGENTS.md`).  
4. 관련 단위 테스트를 맞춘다  
   (`Presentation`, `Usage`, `Profiles` 등 영향 범위).  
5. 구현 중 발견한 **별 이슈**는 이번 범위에 섞지 말고 보고에 남긴다.  
6. WPF UI 스레드 규칙: UI 객체 생성/사용 스레드를 지키고,  
   필요할 때만 `Dispatcher`로 마샬링한다.  
7. 프로세스 종료·인증 파일·프로필 스토어는 **데이터 손실 위험**이 있다.  
   jank 개선 때문에 안전 경로(원자적 쓰기, pause, 단일 인스턴스)를 우회하지 않는다.

---

## Phase 4 — 검증

검증은 **회귀 테스트 + 실측 + (가능하면) 이전 기록 비교**를 함께 한다.  
단위 테스트만 통과했다고 jank 개선 성공으로 단정하지 않는다.

측정 세부·명령·로그 경로: `references/measure-wpf-performance.md`.

### 4.1 자동 회귀

```powershell
dotnet test .\tests\CodexSwitcher.Tests\CodexSwitcher.Tests.csproj
```

관련 시나리오가 있으면 해당 필터만 먼저 돌려도 된다. 예:

```powershell
dotnet test .\tests\CodexSwitcher.Tests\CodexSwitcher.Tests.csproj `
  --filter "FullyQualifiedName~ProfileUsageMonitor|FullyQualifiedName~MainWindowViewModel|FullyQualifiedName~ProfilePresentation"
```

빌드 깨짐·테스트 실패는 성공으로 보고하지 않는다.

### 4.2 실측 계층 (가능하면 모두)

| 계층 | 무엇을 재나 | 이 레포에서 |
|------|-------------|-------------|
| **L1 진단 로그** | 사용량 모니터 구간 등 | `CODEX_SWITCHER_USAGE_DIAGNOSTICS=1` → `%LOCALAPPDATA%\CodexAccountSwitcher\usage-monitor.log` (해당 시나리오만). 전역 기동 타이밍 로그는 없음 |
| **L2 타이밍 테스트** | 유스케이스·모니터·정책 **로직 경로** wall-clock | 기존 Usage/Presentation 테스트에 `Stopwatch`를 넣는 방식, 또는 합의된 전용 타이밍 테스트. (DeckDeckDeck의 `NavigationCacheTimingTests` 대응물은 아직 없음) |
| **L3 실제 exe** | 배포 바이너리 기동·스모크 | publish → 루트 `codexUIUIUI.exe` |
| **L4 체감·프로파일** | 입력 불가·팝업·목록 갱신 | 사용자 체크리스트 / VS Diagnostic Tools |

**해석 규칙 (중요):**

- L2는 ViewModel·스토어·모니터 비용을 잘 보여 준다. **WPF 레이아웃·창 애니메이션·전체 체감과 동일하지 않다.**  
- L1은 사용량 진단이 켜진 경우의 로그일 뿐, 전체 UI fps가 아니다.  
- 보고 시 계층을 표에 명시한다. “테스트 ms = 화면이 N배 부드러움”으로 과장하지 않는다.

### 4.3 이전 기록과 비교 (before / after)

1. **Before 출처를 밝힌다** (우선순위):  
   - 이번 세션 Phase 1에서 남긴 숫자  
   - 동일 타이밍 테스트의 cold/warm 또는 중간값  
   - `usage-monitor.log`의 직전 구간 (날짜·메시지)  
   - 없으면 “before 없음 → after만 기록, 다음 비교용 기준선”이라고 쓴다  
2. **After**는 구현·테스트 통과 **후** 같은 명령·같은 시나리오로 다시 측정한다.  
3. **같은 조건**만 나란히 둔다 (가능하면 Release/exe, 프로필 수 유사).  
4. 비교 표 필수 열:

| 지표 | 계층 | Before | After | 해석 |
|------|------|--------|-------|------|
| 시나리오 완료 중간값 (ms) | L2/L4 | | | |
| 사용량/런타임 갱신 관련 | L1/L2 | | | |
| UI 멈춤 체감 | L4 | | | |
| 빌드 | | Debug/Release/exe | | |

5. 성공 기준: 기획 때 합의값, 또는 “입력 불가 체감 없음” + 회귀 통과.  
6. **목표 미달**이면 숨기지 말고 남은 병목과 다음 안(B/C)을 제안한다.

### 4.4 exe 갱신 (AGENTS.md)

`AGENTS.md`에 따라 **관련 코드 변경 + 테스트 성공 후** 사용자가 더블클릭 실험할 수 있게  
Windows x64 단일 파일 exe를 갱신한다:

```powershell
dotnet test
dotnet publish .\src\CodexSwitcher.Bootstrapper\CodexSwitcher.Bootstrapper.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
Copy-Item `
  .\src\CodexSwitcher.Bootstrapper\bin\Release\net10.0-windows\win-x64\publish\CodexSwitcher.Bootstrapper.exe `
  .\codexUIUIUI.exe -Force
```

주의:

- 테스트 실패 시 루트 exe를 갱신하지 않는다.  
- 단일 인스턴스면 **옛 프로세스가 남으면 최신 빌드가 안 뜰 수 있다** → 종료 후 재실행을 알린다.  
- 파일이 잠겨 있으면 사용자에게 종료를 요청하거나 rename 후 복사 등 안전한 우회를 시도한다.

### 4.5 스모크 체크리스트 (사용자용)

시나리오에 맞게 고른다. 예:

- [ ] 트레이에서 앱 열기·기본 표면 표시가 된다  
- [ ] 프로필 목록·활성 상태가 맞게 보인다  
- [ ] 실행/전환 후 Codex와 UI 상태가 일치한다  
- [ ] 사용량 숫자가 깨지지 않고, 갱신 중에도 목록 조작이 가능하다  
- [ ] 새 프로필 로그인·취소·삭제 확인이 이전과 같다  
- [ ] 팝업 위치 저장/복원이 이상하지 않다  
- [ ] 프로필 0개·다수·비활성 프로필에서 예외로 죽지 않는다  

---

## Phase 5 — 보고

- 무엇을 왜 바꿨는지 (파일 단위, 쉬운 말)  
- **계층별** before/after 숫자 또는 측정 불가 사유  
- 이전 기록 출처 (로그 날짜, 테스트 클래스, Phase 1 메모)  
- 목표 달성 여부 (계층마다 다를 수 있음)  
- 남은 P0/P1  
- 다음에 쓸 안(B/C)이 있으면 한 줄 예고  
- exe 갱신 여부 (`codexUIUIUI.exe`)  

---

## 행동 원칙

1. **측정 가능한 주장** — 코드 근거 + 가능하면 숫자.  
2. **3안 강제** — 기획 단계에서 A/B/C를 빼먹지 않는다.  
3. **선택 존중** — 사용자가 C를 고르면 최소 변경 강요로 되돌리지 않는다. 리스크는 분명히.  
4. **시나리오 순수성** — 측정에 무관한 다른 창 조작·대용량 복사·불필요 전환을 섞지 않는다.  
5. **UI 스레드 vs 백그라운드** — 멈춤이 Dispatcher 블로킹인지, 디스크/프로세스/CLI인지, 레이아웃인지 구분한다.  
6. **보안** — 비밀키·토큰·인증 파일 내용을 로그/테스트에 넣지 않는다.  
7. **프로젝트 지침** — `AGENTS.md` 및 clean architecture 우선.  
8. **Android 절차 금지** — `adb`, gfxinfo, Compose 리컴포즈 용어로 이 앱을 진단하지 않는다.  
9. **데이터 안전** — jank 개선을 위해 프로필/인증 원자적 쓰기·삭제 확인·단일 인스턴스를 깨지 않는다.

---

## 안티패턴

- 분석 없이 바로 대규모 리팩터  
- 3안 없이 “이 한 가지로 갑니다”  
- before 없이 after만 보고 성공 선언  
- 사용량 pause·배치 드레인·백오프를 “복잡하다”는 이유로 측정 없이 제거  
- Core(Domain/UseCases)에 UI·Dispatcher·Bitmap 타입을 끌어올림  
- 요청 범위 밖 포맷 변경·파일 정리 섞기  
- DeckDeckDeck 전용 경로(`StartupTimingLog`, 썸네일 캐시, `NumpadPromptLauncher` 로그)를 이 레포에 있다고 가정  

---

## 빠른 체크리스트

- [ ] 시나리오 한 문장 (WPF/CodexSwitcher 맥락)  
- [ ] P0 병목 + 근거 파일  
- [ ] before 측정·출처 (또는 불가 사유 / 대리 기준)  
- [ ] 안 A / B / C 제시 + 추천 + 아키텍처 충돌 메모  
- [ ] 사용자 선택  
- [ ] 구현  
- [ ] `dotnet test` 회귀  
- [ ] L2 타이밍(해당 시) + L1 진단 로그(해당 시) + L3/L4  
- [ ] before/after 표 (계층 열 포함)  
- [ ] 필요 시 publish / `codexUIUIUI.exe`  
- [ ] 스모크 체크리스트 + 남은 이슈  
