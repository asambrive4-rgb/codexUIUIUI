# WPF / Windows 성능 측정 메모

`reduce-ui-jank` Phase 1·4에서 쓰는 **Windows WPF** 측정 참고.  
Android `adb` / `dumpsys gfxinfo` 는 사용하지 않는다.

대상 앱: **Codex Account Switcher** (`codexUIUIUI` / `CodexSwitcher`).

---

## 0. 측정 원칙

1. **시나리오를 한 문장으로 고정**한 뒤 before/after를 같은 조건으로 비교한다.  
2. 가능하면 **Release** (또는 루트 `codexUIUIUI.exe`) 로 체감 측정한다. Debug는 상대 비교만.  
3. 측정 중 **다른 창 조작·대용량 파일 복사·불필요 프로필 전환**을 섞지 않는다.  
4. “janky 100%” 같은 모바일 지표 대신, 이 앱에서는  
   **완료 시간(ms) + UI 멈춤 여부 + (가능 시) 프로파일러 샘플** 을 쓴다.  
5. before 없이 after만 보고 성공 선언하지 않는다.  
6. 프로필 수·활성 프로필 유무를 before/after에 맞춘다 (폴링 비용이 달라진다).

---

## 1. 가벼운 측정 (도구 설치 최소)

### 1.1 스톱워치 / 체감 체크리스트

사용자가 직접:

1. 시나리오 직전 상태까지 만든다.  
2. 동작 시작과 동시에 스톱워치.  
3. “완료로 정의한 순간”(예: 목록 입력 가능, 팝업 표시, 전환 메시지 안정)에 정지.  
4. 3회 측정 → 중간값 기록.

기록 예:

| 회차 | ms | 메모 |
|------|-----|------|
| 1 | | |
| 2 | | |
| 3 | | |
| 중간값 | | |

### 1.2 사용량 모니터 진단 로그 — L1 (해당 시나리오)

이 앱에는 DeckDeckDeck 식 전역 `StartupTimingLog` / `app.log` 기동 구간이 **없다**.  
사용량 폴링 관련 시나리오에서는 선택적으로 진단 로그를 켠다.

- 코드: `src/CodexSwitcher.Bootstrapper/Usage/UsageMonitorDiagnostics.cs`
- 환경 변수: `CODEX_SWITCHER_USAGE_DIAGNOSTICS=1` (또는 `true`)
- 로그 파일: `%LOCALAPPDATA%\CodexAccountSwitcher\usage-monitor.log`
- 기본값: 꺼짐 (상시 I/O 비용 없음)

**S-usage** before/after에 유용. 기동·트레이·전환 전체의 대체 지표로 쓰지 말 것.

측정 팁:

- 동일 PC, 가능하면 동일 전원 상태  
- **직전 실행 프로세스를 종료한 뒤** 측정 (단일 인스턴스면 재실행이 옛 창만 연다)  
- before/after 모두 진단 ON 여부를 같게 유지  
- 검증 후 진단 환경 변수를 끄라고 안내  

PowerShell 예:

```powershell
$env:CODEX_SWITCHER_USAGE_DIAGNOSTICS = "1"
# 앱 실행 후:
Get-Content "$env:LOCALAPPDATA\CodexAccountSwitcher\usage-monitor.log" -Tail 40
```

### 1.3 타이밍 단위 테스트 (로직 경로 wall-clock) — L2

제품 코드에 상시 진단 로그를 넣지 않고도, **테스트에서 `Stopwatch`로 경로 비용**을 잴 수 있다.

이 레포 기준 (현재 상태):

| 영역 | 테스트 위치 | 해석 예 |
|------|-------------|---------|
| 사용량 모니터 | `Usage/ProfileUsageMonitorTests` | 갱신 사이클·pause 동작 |
| 갱신 정책 | `Usage/ProfileUsageRefreshPolicyTests` | 간격·백오프 (순수 로직, 매우 빠름) |
| 목록/메시지 표시 | `Presentation/*` | VM 갱신·메시지 포맷 |
| 스토어/유스케이스 | `Profiles/*` | I/O·전환 로직 경로 |

전용 `*TimingTests` 가 없으면:

- 분석·검증 합의 하에 **임시 타이밍 테스트**를 추가하거나  
- 기존 테스트에 `ITestOutputHelper` + 중간값 출력을 넣을 수 있다  
- 끝나면 유지할지 제거할지 보고에 적는다  

```powershell
dotnet test .\tests\CodexSwitcher.Tests\CodexSwitcher.Tests.csproj `
  --filter "FullyQualifiedName~ProfileUsageMonitor" `
  --logger "console;verbosity=detailed"
```

작성·유지 규칙:

- 샘플 여러 번 → **중간값(median)** 보고 (첫 회 JIT/워밍업 스파이크 분리)  
- 절대 ms 하드 임계는 환경마다 깨지기 쉽다 → **상대 비교** + 느슨한 상한  
- 시드 프로필 수·실패 횟수를 고정해 before/after 조건을 맞춘다  
- **Core에 Dispatcher·Bitmap·UI 캐시 타입을 끌어오지 않는다** — 측정은 테스트·Bootstrapper·Infra 경계에서  

한계: L2는 **로직·I/O 경로**다. 창 그림자·팝업 placement·레이아웃 체감은 L4.

### 1.4 임시 Stopwatch 계측 (분석·검증용)

짧은 구간만 볼 때 **합의된 진단 코드**를 제품에 넣을 수 있다.

- `Stopwatch`로 구간 ms 기록  
- Debug/진단 빌드 또는 환경 변수 가드 권장  
- **검증 후 제거**하거나, 상시가 필요하면 파일 로거에만 남기고 UI에 노출하지 않는다  
- 인증 파일 내용·전체 경로·토큰을 로그에 넣지 않는다  

기동 구간(S-start) 전용 상시 로거가 필요해지면, 그때 `StartupTimingLog` 패턴을  
**별도 합의**로 Bootstrapper에 도입할지 기획 3안에 넣는다. 분석 단계에서 있다고 가정하지 말 것.

---

## 2. Visual Studio Diagnostic Tools (권장, 개발 PC)

대상: UI 스레드 블로킹, CPU 핫스팟, 할당.

1. Visual Studio에서 `CodexSwitcher.Bootstrapper` 디버그 실행  
2. **Diagnostic Tools** 창에서 CPU Usage / Events 확인  
3. 시나리오 재현  
4. 샘플에서 상위 스택 확인:

| 보이는 패턴 | 해석 단서 |
|-------------|-----------|
| `FileStream` / DPAPI / ACL | 프로필 스토어·자격 증명 I/O가 UI 경로 |
| `Process` / wait / Codex CLI | 실행·전환·rate limit reader가 UI를 막음 |
| `Measure` / `Arrange` / `OnRender` | 레이아웃·렌더 thrash |
| `PropertyChanged` / 바인딩 업데이트 폭주 | 과도한 알림·큰 트리 갱신 |
| `PeriodicTimer` / monitor 루프 | 폴링 주기와 UI 드레인 불균형 |

before/after 비교 시 **같은 시나리오 구간**의 상위 스택·블로킹 시간을 메모한다.

---

## 3. WPF 성능 관련 추가 옵션

환경에 따라 사용 가능하면 사용하고, 없으면 “도구 없음”으로 적고 1·2항에 의존한다.

| 도구 | 용도 |
|------|------|
| Visual Studio **Timeline** / Performance Profiler | UI 스레드 vs 렌더, 이벤트 타임라인 |
| **Perfetto** / ETW (고급) | 시스템 전역 지연 — 필요할 때만 |
| **PresentMon** 등 프레임 도구 | 전체 화면 애니메이션 fps — 이 앱은 작은 창이라 우선순위 낮음 |

이 앱은 게임형 60fps 풀스크린이 아니다.  
**입력 지연·팝업/창 표시·목록·사용량 갱신 완료**가 1순위 지표다.

---

## 4. 시나리오별 측정 프로토콜

### 공통

1. 앱을 시나리오 **직전** 상태까지 만든다.  
2. 카운터/스톱워치/프로파일러 구간 시작.  
3. **순수 동작만** 수행 (불필요 클릭·창 이동 자제).  
4. 완료 조건에 도달하면 정지·저장.  
5. 로그/표를 파일로 남길 때는 사용자가 요청하거나 에이전트가 합의했을 때만.  
   (기본적으로 대화에 숫자만 남겨도 됨.)

### S-start — 기동~첫 표면

- 완료: 트레이 아이콘 준비, (정책상) 첫 창/팝업 표시·입력 가능  
- 지표: 스톱워치 ms, 프로파일러 상위 스택  
- 단서: `App.OnStartup`, 유스케이스 조립, `TrayIconService`, `ShowDefaultSurface`

### S-tray — 트레이 재표시

- 완료: 메인 창 또는 기본 팝업이 포그라운드·입력 가능  
- 단서: `TrayIconService` → `Dispatcher.BeginInvoke`, `Show` / `ShowDefaultSurface`

### S-popup — 프로필 팝업

- 완료: 팝업 표시 + 위치 적용 + 목록 상호작용 가능  
- 단서: `ProfilePopupWindow`, `PopupPlacementStore`  
- 주의: placement 파일 I/O가 UI를 막는지

### S-list — 목록·런타임 상태

- 완료: 프로필 행·활성/실행 상태가 의미 있게 표시  
- 단서: `MainWindowViewModel`, `RefreshRuntimeState`, `RuntimeMonitorInterval` (3초)

### S-usage — 사용량 갱신

- 완료: 보이는 한도 숫자/레벨이 안정, 입력 가능  
- 단서: `ProfileUsageMonitor`, `ProfileUsageRefreshPolicy` (활성 10초 등),  
  `WindowsCodexRateLimitReader`, UI 스냅샷 배치 드레인  
- 지표: 진단 로그 간격, 수동 새로고침 wall-clock, 갱신 중 클릭 응답

### S-run / S-switch

- 완료: 결과 메시지·활성 프로필 표시, UI 재입력 가능  
- 단서: `RunProfileUseCase` / `SwitchProfileUseCase`, 프로세스 종료·재실행,  
  작업 중 `usageMonitor.PauseAsync`  
- **주의:** 실제 Codex 프로세스 제어는 부작용이 큼. 가능하면 테스트 더블/모의 경로로 L2,  
  실기 L3/L4는 사용자 확인 하에

### S-login / S-delete

- 완료: 대화상자 닫힘 + 목록 반영  
- 단서: `NewProfileWindow`, `DeleteProfileConfirmationWindow`, login controller

---

## 5. 해석 가이드

| 관찰 | 우선 의심 |
|------|-----------|
| 클릭 후 수 100ms~수 초 입력 불가 | UI 스레드 동기 작업 (I/O, 프로세스 wait, 무거운 동기 호출) |
| 입력은 되는데 사용량 숫자만 늦게 참 | 백그라운드 probe·캐시 미스 (체감 품질) |
| 전환 직후 끝에서 멈칫 | 전환 후 목록/런타임 재조회, 스냅샷 일괄 바인딩 |
| 주기적 끊김 (수 초마다) | `RuntimeMonitorInterval` / usage refresh 주기와 UI 드레인 |
| 기동만 느림 | 트레이 아이콘, 스토어 첫 읽기, 첫 Show 경로 |

**GPU vs UI:**  
WPF에서 “느림”의 대부분은 **Dispatcher(UI) 작업** 또는 **동기 I/O·프로세스 대기**다.  
작은 트레이 앱이므로 PresentMon fps보다 **입력 지연·완료 시간**을 우선한다.

---

## 6. before / after 표 템플릿 (이전 기록 비교)

**Before 출처를 한 줄로 적는다** (로그 날짜, 테스트 클래스, Phase 1 메모 중 하나).

| 지표 | 계층 | Before (출처) | After | 해석 |
|------|------|---------------|-------|------|
| 시나리오 완료 중간값 (ms) | L2/L4 | | | |
| 최악 1회 (ms) | L2/L4 | | | |
| 사용량 진단 관련 | L1 | | | |
| UI 입력 불가 체감 | L4 | 있음/없음 | | |
| 프로파일러 상위 1스택 | L4 | | | |
| 프로필 수 / 빌드 | | | | |

**이전 기록이 없을 때**

- L2: 같은 런에서 느린 경로 vs 개선 경로를 대리 비교로 써도 된다. 단, “구현 전 전용 before 런은 없음”을 명시.  
- L1: 진단 로그에 남은 과거 줄을 before로 쓸 수 있으면 날짜와 함께.  
- 둘 다 없으면 after만 기준선으로 남기고, 다음 `/reduce-ui-jank`에서 비교한다고 적는다.

성공 기준 예:

- 중간값 N% 감소  
- “입력 불가 구간 체감 없음” + 회귀 테스트 통과  
- 기동 안을 안 건드렸으면 기동 시간 동결(변화 없음)도 정상  

---

## 7. 자동 검증과의 관계

측정은 **체감·프로파일·타이밍 테스트**이고, 회귀는 **기능 테스트**로 막는다.

```powershell
dotnet test .\tests\CodexSwitcher.Tests\CodexSwitcher.Tests.csproj
```

사용량·Presentation·Profiles 관련 테스트를 우선 실행.  
테스트가 통과해도 jank가 남을 수 있다 — L1~L4를 구분해 보고한다.

관련 코드 변경 + 테스트 성공 후 루트 exe (`AGENTS.md`):

```powershell
dotnet publish .\src\CodexSwitcher.Bootstrapper\CodexSwitcher.Bootstrapper.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
Copy-Item `
  .\src\CodexSwitcher.Bootstrapper\bin\Release\net10.0-windows\win-x64\publish\CodexSwitcher.Bootstrapper.exe `
  .\codexUIUIUI.exe -Force
```

---

## 8. 안티패턴

- Debug 한 번 측정으로 “N배 빨라짐” 단정  
- 창 애니메이션 duration을 성능 개선으로 보고  
- 측정 중 다른 앱 풀스크린·대용량 복사  
- adb/gfxinfo 수치를 이 프로젝트 보고서에 인용  
- before 시나리오와 after 시나리오가 다름 (프로필 수·활성 여부 불일치)  
- DeckDeckDeck 로그 경로(`%APPDATA%\NumpadPromptLauncher\`)를 이 앱 지표로 인용  
- 인증·프로필 파일을 측정 로그에 덤프  
