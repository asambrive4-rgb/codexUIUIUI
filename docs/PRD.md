# PRD v2: Codex Windows 데스크톱 앱 계정 전환 도구

> 문서 상태: Draft v2  
> 대상 플랫폼: Windows  
> 제품 유형: 개인용 로컬 데스크톱 유틸리티  
> 기술 방향: PowerShell PoC → C# / .NET 10 / WPF MVP  
> 기본 아키텍처: Clean Architecture + MVVM

---

## 1. 문서 목적

이 문서는 Windows용 Codex 데스크톱 앱에서 여러 OpenAI/ChatGPT 계정을 번갈아 사용하는 개인 사용자를 위한 **Codex 전용 계정 전환 도구**의 제품 요구사항과 기술 방향을 정의한다.

본 개정본은 다음 내용을 반영한다.

- 최초 로그인은 기존 Codex 로그인 절차를 사용한다.
- 계정 전환은 ChatGPT 웹 세션에 어떠한 영향도 주어서는 안 된다.
- 사용자는 수동으로 Codex를 종료하지 않고 `전환` 동작만 수행한다.
- 기술적으로 재시작이 필요하면 도구가 내부적으로 안전하게 처리한다.
- 프로필 삭제 시 인증 데이터까지 삭제하되 확인 팝업을 제공한다.
- 개발 순서는 PowerShell 기반 기술 검증 후 WPF GUI MVP로 진행한다.
- 개인 로컬 도구 수준의 보안을 적용한다.
- Clean Architecture와 MVVM을 기본 아키텍처 골조로 사용한다.

---

## 2. 제품 한 줄 정의

> ChatGPT 웹 로그인 상태를 그대로 유지하면서, Windows용 Codex 데스크톱 앱에서 사용할 계정만 빠르고 안전하게 전환하는 로컬 계정 런처.

---

## 3. 배경

현재 사용자는 Codex Windows 데스크톱 앱에서 다른 계정으로 전환하기 위해 직접 로그아웃하고 브라우저에서 다시 로그인해야 한다.

이 과정에서 Codex 인증뿐 아니라 일반 브라우저에서 열어 둔 ChatGPT 웹 세션까지 영향을 받을 수 있다. 그 결과 다음 문제가 발생한다.

- 기존 ChatGPT 웹 계정이 로그아웃된다.
- 열어 둔 채팅방과 작업 맥락이 끊긴다.
- 개인 계정과 회사 계정을 병행하기 어렵다.
- 계정 전환을 반복할수록 로그인 절차가 번거로워진다.
- 사용자가 현재 Codex에서 어떤 계정을 사용 중인지 관리하기 어렵다.

---

## 4. 문제 정의

### 4.1 현재 문제

- Codex 계정 전환이 일반 브라우저의 ChatGPT 로그인 상태와 분리되어 있지 않다.
- 사용자가 계정마다 Codex 인증 상태를 독립적으로 보관하기 어렵다.
- 반복 전환 시 매번 로그인 절차를 수행해야 한다.
- 계정 전환을 위해 Codex 종료, 인증 파일 변경, 재실행 등을 사용자가 직접 처리해야 할 수 있다.

### 4.2 핵심 문제

> Codex 인증 환경과 평소 사용하는 브라우저의 ChatGPT 웹 세션을 분리하고, 계정별 Codex 인증 상태를 로컬에서 독립적으로 유지해야 한다.

### 4.3 해결 가설

계정별로 Codex의 로컬 인증 및 설정 저장소를 분리하고, 선택한 저장소를 적용한 상태로 Codex를 실행하면 다음 상태를 만들 수 있다.

```text
일반 브라우저
└── 기존 ChatGPT 웹 계정 및 채팅 세션 유지

Codex Account Switcher
├── Personal 프로필
├── Work 프로필
└── Test 프로필
        ↓
선택한 프로필의 독립된 Codex 환경으로 앱 실행
```

가장 먼저 검증할 구현 후보는 프로필별 `CODEX_HOME` 분리이다. 이 방식이 Windows 데스크톱 앱에서 동작하지 않을 경우에만 인증 파일 스냅샷 교체 등 대체 방식을 검토한다.

---

## 5. 확정된 제품 결정

| 항목 | 결정 |
|---|---|
| 최초 로그인 | 기존 Codex 로그인 절차 사용 |
| 웹 세션 보호 | 기존 ChatGPT 웹 계정, 로그인, 채팅방에 영향을 주면 안 됨 |
| 사용자 전환 경험 | 사용자는 `전환` 버튼만 누름 |
| 내부 재시작 | 기술적으로 필요하면 도구가 Codex를 종료하고 재실행 |
| 동시 실행 | 서로 다른 계정의 Codex 동시 실행은 지원하지 않음 |
| 프로필 삭제 | 프로필 메타데이터와 인증 데이터를 함께 삭제 |
| 삭제 안전장치 | 삭제 전 확인 팝업 1회 제공 |
| 개발 순서 | PowerShell PoC → WPF GUI MVP → 필요 시 트레이 앱 |
| 보안 수준 | 개인 로컬 도구 수준, 현재 Windows 사용자 범위 보호 |
| 기본 아키텍처 | Clean Architecture + MVVM |

---

## 6. 목표

### 6.1 제품 목표

- ChatGPT 웹 세션을 유지한 채 Codex 계정만 전환할 수 있게 한다.
- 여러 Codex 계정의 인증 상태를 프로필 단위로 분리한다.
- 사용자가 1~2번의 클릭으로 원하는 계정으로 전환할 수 있게 한다.
- 로그인 정보를 클라우드나 외부 서버에 저장하지 않는다.
- Codex 내부 구현이나 인증 방식이 바뀌어도 영향 범위를 외부 어댑터에 제한한다.

### 6.2 사용자 목표

- 개인·회사·테스트 계정을 빠르게 오가고 싶다.
- ChatGPT 웹 작업은 끊기지 않았으면 좋겠다.
- 최초 로그인 이후에는 매번 다시 로그인하고 싶지 않다.
- 계정 전환을 위해 Codex를 직접 종료하거나 파일을 바꾸고 싶지 않다.
- 현재 어떤 Codex 프로필을 사용 중인지 즉시 확인하고 싶다.

### 6.3 기술 목표

- 핵심 정책과 Windows 구현 세부사항을 분리한다.
- UI 프레임워크가 계정 전환 규칙을 소유하지 않게 한다.
- 프로필, 전환, 삭제 흐름을 독립된 Use Case로 구현한다.
- 실제 Codex, 파일시스템, 레지스트리, Windows API 없이 핵심 Use Case를 테스트할 수 있게 한다.

---

## 7. 비목표

MVP에서는 다음을 지원하지 않는다.

- OpenAI 공식 Codex 앱 내부 코드 수정
- ChatGPT 웹사이트의 계정 전환 기능
- 여러 Codex 계정의 동시 실행
- 여러 Windows 사용자 간 프로필 공유
- 조직 또는 워크스페이스 관리자 기능
- 중앙 서버 기반 계정 관리
- 인증 토큰의 클라우드 저장 또는 동기화
- 이메일 및 비밀번호 직접 수집
- 자체 OAuth 또는 로그인 시스템 구현
- 프로필 간 Codex 대화 기록 병합
- macOS 또는 Linux 지원
- 자동 업데이트 시스템
- 엔터프라이즈급 비밀 관리 시스템

---

## 8. 대상 사용자

### 8.1 1차 사용자

- Windows용 Codex 데스크톱 앱을 사용하는 개인 사용자
- 개인·회사·테스트 계정을 번갈아 사용하는 사용자
- Codex와 ChatGPT 웹을 동시에 사용하는 사용자
- 프로젝트별로 다른 Codex 계정을 사용해야 하는 사용자

### 8.2 예시 프로필

| 프로필 이름 | 용도 |
|---|---|
| Personal | 개인 작업용 계정 |
| Work | 회사 업무용 계정 |
| Test | 기능 검증용 계정 |
| Project A | 특정 프로젝트 전용 계정 |

---

## 9. 핵심 사용자 흐름

### 9.1 최초 프로필 생성 및 로그인

1. 사용자가 전환 도구를 실행한다.
2. `새 프로필 추가`를 선택한다.
3. 프로필 이름을 입력한다.
4. 도구가 프로필 전용 Codex 저장소를 생성한다.
5. 해당 저장소가 적용된 상태로 Codex를 실행한다.
6. 사용자는 기존 Codex 로그인 절차를 통해 로그인한다.
7. 인증 상태는 해당 프로필 전용 저장소에 기록된다.
8. 기존 브라우저의 ChatGPT 로그인 상태와 채팅 세션은 그대로 유지된다.

### 9.2 등록된 프로필로 Codex 실행

1. 사용자가 프로필 목록에서 원하는 프로필을 선택한다.
2. `실행` 또는 `전환` 버튼을 누른다.
3. 도구가 Codex 실행 여부를 확인한다.
4. Codex가 실행 중이 아니면 선택 프로필 환경으로 실행한다.
5. 선택한 계정으로 Codex가 열린다.

### 9.3 실행 중 다른 계정으로 전환

1. 사용자가 다른 프로필의 `전환` 버튼을 누른다.
2. 도구가 현재 Codex 상태를 확인한다.
3. 필요한 경우 정상 종료를 요청한다.
4. 종료 완료를 확인한다.
5. 새 프로필의 Codex 환경을 적용한다.
6. Codex를 다시 실행한다.
7. UI에 새 활성 프로필을 표시한다.

사용자는 종료·재실행 과정을 직접 수행하지 않는다. 사용자 관점에서는 하나의 `계정 전환` 동작이다.

### 9.4 프로필 삭제

1. 사용자가 프로필의 `삭제`를 선택한다.
2. 도구가 삭제 확인 팝업을 표시한다.
3. 팝업은 인증 데이터가 함께 삭제되며 복구할 수 없음을 알린다.
4. 사용자가 확정하면 해당 프로필의 메타데이터와 Codex 인증 저장소를 삭제한다.
5. 삭제된 프로필은 목록에서 제거된다.

현재 활성 프로필이거나 Codex가 해당 프로필로 실행 중인 경우에는 먼저 종료한 뒤 삭제하거나 삭제를 차단하고 안내한다.

---

## 10. 기능 요구사항

### FR-01. 프로필 목록 조회

- 앱 시작 시 등록된 프로필을 표시해야 한다.
- 각 프로필에는 이름, 마지막 사용 시각, 상태를 표시할 수 있어야 한다.
- 현재 활성 프로필을 시각적으로 구분해야 한다.

### FR-02. 프로필 생성

- 사용자는 새 프로필 이름을 입력할 수 있어야 한다.
- 이름은 공백만으로 구성될 수 없다.
- 이름 비교 시 대소문자를 무시한 중복을 허용하지 않는다.
- 프로필별 고유 ID와 독립 저장 경로를 생성해야 한다.
- 생성 시 인증 토큰이나 비밀번호를 직접 입력받지 않는다.

### FR-03. 최초 로그인

- 새 프로필은 기존 Codex 로그인 절차를 통해 인증되어야 한다.
- 전환 도구는 OpenAI 이메일, 비밀번호, 토큰을 직접 수집하지 않는다.
- 로그인 결과는 프로필별 Codex 저장소에 보관되어야 한다.
- 로그인 과정 전후로 일반 브라우저의 기존 ChatGPT 세션이 변경되지 않아야 한다.

### FR-04. 프로필별 인증 저장소 분리

- 각 프로필은 독립된 Codex 인증 및 설정 저장소를 가져야 한다.
- 1순위 방식은 프로필별 `CODEX_HOME`이다.
- 인증 저장 위치를 파일 기반으로 고정해야 분리가 가능한지 PoC에서 검증한다.
- 프로필 간 인증 파일, 캐시, 로그, 설정이 섞이지 않아야 한다.

### FR-05. 선택 프로필로 Codex 실행

- 선택된 프로필 경로를 적용한 상태로 Codex를 실행해야 한다.
- 실행 성공 여부를 사용자에게 표시해야 한다.
- Codex 실행 파일 또는 앱 패키지를 찾지 못하면 해결 가능한 오류 메시지를 제공해야 한다.

### FR-06. 계정 전환

- 사용자는 실행 중인 Codex를 직접 종료하지 않고 다른 프로필로 전환할 수 있어야 한다.
- 현재 프로필과 선택 프로필이 같으면 불필요한 재시작을 하지 않아야 한다.
- 다른 프로필로 전환할 때 기술적으로 프로세스 재시작이 필요하면 자동으로 수행해야 한다.
- 동시 실행은 허용하지 않는다.
- 정상 종료가 실패하면 강제 종료 전 확인 또는 명확한 안내를 제공해야 한다.

### FR-07. 프로필 삭제

- 삭제 전 확인 팝업을 표시해야 한다.
- 확인 문구에는 인증 데이터와 로컬 상태가 함께 삭제됨을 명시해야 한다.
- 확정 시 프로필 메타데이터와 인증 저장소를 모두 삭제해야 한다.
- 삭제 후 복구 기능은 MVP에서 제공하지 않는다.

### FR-08. 웹 세션 비간섭

계정 생성, 로그인, 실행, 전환, 삭제 중 다음 상태가 유지되어야 한다.

- `chatgpt.com`의 기존 로그인 상태 유지
- 기존 로그인 계정 유지
- 열려 있던 채팅방 접근 가능
- 사용자의 명시적 행동 없이 브라우저 로그아웃 금지
- 사용자의 명시적 행동 없이 웹 계정 변경 금지

이 요구사항을 만족하지 못하면 MVP 출시 조건을 충족하지 못한 것으로 판단한다.

### FR-09. 활성 프로필 표시

- 현재 또는 마지막으로 실행한 프로필을 표시해야 한다.
- 활성 상태는 전환 도구가 관리하는 정보이며 Codex 내부의 실제 계정과 불일치할 수 있음을 고려해야 한다.
- 가능한 경우 실행 성공 후 상태를 갱신한다.

### FR-10. 오류 처리

최소한 다음 오류를 구분해야 한다.

- Codex 설치를 찾을 수 없음
- 프로필 저장소 생성 실패
- 프로필 저장소 접근 권한 없음
- Codex 종료 실패
- Codex 실행 실패
- 인증 저장소가 분리되지 않음
- 프로필 데이터 손상
- 프로필 삭제 실패

오류 메시지는 토큰, 경로 내 민감정보, 인증 파일 내용을 노출하지 않아야 한다.

---

## 11. 비기능 요구사항

### NFR-01. 사용성

- 일반적인 프로필 전환은 1~2번의 클릭으로 완료되어야 한다.
- 프로필 이름과 현재 상태를 한눈에 확인할 수 있어야 한다.
- 사용자가 프로세스, 환경변수, 인증 파일을 직접 다루지 않아야 한다.

### NFR-02. 성능

- 프로필 목록은 앱 실행 후 즉시 표시되어야 한다.
- 전환 도구 자체의 유휴 메모리 사용량은 개인용 유틸리티 수준으로 유지한다.
- 전환 시간의 대부분은 Codex 종료 및 실행 시간에 의해 결정되므로, 전환 도구는 불필요한 지연을 추가하지 않아야 한다.

### NFR-03. 안정성

- 전환 중 실패하더라도 기존 프로필 데이터가 손상되지 않아야 한다.
- 인증 파일을 직접 교체하는 폴백 방식은 원자적 교체와 복구 전략이 검증되기 전에는 사용하지 않는다.
- 중복 실행으로 인해 서로 다른 프로필이 동시에 활성화되지 않도록 단일 인스턴스 정책을 적용한다.

### NFR-04. 보안

- 모든 데이터는 로컬 PC에만 저장한다.
- 저장 위치는 `%LOCALAPPDATA%` 하위로 제한한다.
- 현재 Windows 사용자만 접근하도록 파일 및 폴더 권한을 제한한다.
- 인증 파일 내용은 앱에서 불필요하게 읽거나 파싱하지 않는다.
- 토큰, 쿠키, 인증 파일 내용은 로그에 남기지 않는다.
- 인증 데이터는 Git, 클라우드 동기화 폴더, 임시 공유 폴더에 저장하지 않는다.
- MVP에서는 자체 마스터 비밀번호, 별도 암호화 저장소, 클라우드 백업을 제공하지 않는다.

### NFR-05. 유지보수성

- Codex 실행 방식, 저장 경로, Windows API는 핵심 정책과 분리한다.
- Codex 업데이트로 세부 구현이 바뀌면 외부 어댑터만 교체할 수 있어야 한다.
- 핵심 Use Case는 WPF, 파일시스템, 실제 Codex 프로세스 없이 테스트 가능해야 한다.

### NFR-06. 관찰 가능성

- 앱 로그는 동작 단계와 실패 유형만 기록한다.
- 인증 데이터, 전체 환경변수, `auth.json` 내용은 기록하지 않는다.
- 사용자가 문제를 재현할 수 있도록 이벤트 ID 또는 오류 코드를 제공할 수 있다.

---

## 12. 기술 스택

### 12.1 단계별 스택

| 단계 | 기술 | 목적 |
|---|---|---|
| 기술 검증 | PowerShell 7 | `CODEX_HOME`, 프로세스 실행, 인증 분리 여부 검증 |
| PoC 테스트 | Pester | PowerShell 함수 및 파일 동작 검증 |
| GUI MVP | C# / .NET 10 LTS | Windows 네이티브 통합 및 장기 유지보수 |
| UI | WPF | 가벼운 Windows 데스크톱 GUI |
| UI 패턴 | MVVM | 화면 상태와 UI 로직 분리 |
| 직렬화 | `System.Text.Json` | 로컬 프로필 메타데이터 저장 |
| 프로세스 제어 | `System.Diagnostics` | Codex 탐색, 종료, 실행 |
| 단위 테스트 | xUnit | Domain 및 Application Use Case 테스트 |
| 배포 | Self-contained, win-x64 | 별도 .NET 설치 없는 단일 사용자 배포 |

### 12.2 배포 기본값

- `win-x64`
- self-contained
- single-file
- trimming 비활성화
- 초기에는 설치 프로그램 없이 실행 파일 배포 가능
- 사용자 확대 후 코드 서명, MSIX, 자동 업데이트 검토

---

## 13. 아키텍처 원칙

### 13.1 Clean Architecture 기본 원칙

- 소스 코드 의존성은 바깥에서 안쪽으로 향한다.
- Domain과 Application은 WPF, Windows API, 파일시스템, Codex 실행 방식에 의존하지 않는다.
- 프로필 생성, 전환, 삭제 규칙은 Use Case가 소유한다.
- 파일시스템, 프로세스, 운영체제 권한, Codex 실행은 외부 어댑터로 취급한다.
- UI, ViewModel, Repository, Process Adapter는 비즈니스 규칙을 소유하지 않는다.
- 공용 `Utils`, 범용 `Service`, 거대한 Manager 클래스에 정책을 숨기지 않는다.
- 폴더 이름만 나눈 가짜 계층이 아니라 프로젝트 참조로 의존성 방향을 강제한다.
- 핵심 정책 테스트는 실제 WPF, Codex, 파일시스템, 네트워크 없이 실행 가능해야 한다.

### 13.2 MVVM 적용 원칙

- View는 화면 표현과 바인딩만 담당한다.
- ViewModel은 화면 상태, 명령, 입력 검증 피드백을 담당한다.
- ViewModel은 파일을 직접 삭제하거나 프로세스를 직접 실행하지 않는다.
- ViewModel은 Application 계층의 Use Case를 호출한다.
- Use Case 결과는 Presentation Model로 변환하여 화면에 표시한다.
- `MessageBox`, 파일 선택기, 다이얼로그는 UI 포트를 통해 추상화한다.
- 비즈니스 규칙을 XAML 코드비하인드나 Command 내부에 넣지 않는다.
- 코드비하인드는 창 이동, 포커스 등 순수 UI 동작에 한해 최소화한다.

### 13.3 의존성 방향

```text
CodexSwitcher.Presentation.Wpf
        │
        ├───────────────┐
        ▼               ▼
CodexSwitcher.Application ◀── CodexSwitcher.Infrastructure.Windows
        │                              │
        ▼                              │ implements ports
CodexSwitcher.Domain ◀─────────────────┘

CodexSwitcher.Bootstrapper
└── 모든 구현을 조립하는 Composition Root
```

허용되는 프로젝트 참조 방향:

```text
Presentation.Wpf   → Application → Domain
Infrastructure     → Application → Domain
Bootstrapper       → Presentation + Infrastructure + Application
Domain             → 아무 외부 프로젝트에도 의존하지 않음
```

금지되는 참조:

```text
Domain → WPF
Domain → Windows API
Application → Infrastructure.Windows
Application → System.Diagnostics.Process
ViewModel → 실제 파일시스템
View → ProfileRepository
Infrastructure → ViewModel
```

---

## 14. 권장 솔루션 구조

기술 계층만으로 나누기보다 Use Case가 드러나도록 기능별 폴더를 사용한다.

```text
CodexAccountSwitcher.sln
│
├── src/
│   ├── CodexSwitcher.Domain/
│   │   ├── Profiles/
│   │   │   ├── CodexProfile.cs
│   │   │   ├── ProfileId.cs
│   │   │   ├── ProfileName.cs
│   │   │   └── ProfileErrors.cs
│   │   └── Shared/
│   │       └── Result.cs
│   │
│   ├── CodexSwitcher.Application/
│   │   ├── Profiles/
│   │   │   ├── ListProfiles/
│   │   │   ├── CreateProfile/
│   │   │   ├── RenameProfile/
│   │   │   └── DeleteProfile/
│   │   ├── CodexSessions/
│   │   │   ├── LaunchProfile/
│   │   │   ├── SwitchProfile/
│   │   │   └── GetActiveProfile/
│   │   └── Ports/
│   │       ├── IProfileRepository.cs
│   │       ├── IProfileStorage.cs
│   │       ├── ICodexProcessGateway.cs
│   │       ├── ICodexLauncher.cs
│   │       ├── IFilePermissionGateway.cs
│   │       └── IClock.cs
│   │
│   ├── CodexSwitcher.Infrastructure.Windows/
│   │   ├── Profiles/
│   │   │   ├── JsonProfileRepository.cs
│   │   │   └── LocalProfileStorage.cs
│   │   ├── Codex/
│   │   │   ├── WindowsCodexLocator.cs
│   │   │   ├── WindowsCodexProcessGateway.cs
│   │   │   └── WindowsCodexLauncher.cs
│   │   ├── Security/
│   │   │   └── NtfsPermissionGateway.cs
│   │   └── Time/
│   │       └── SystemClock.cs
│   │
│   ├── CodexSwitcher.Presentation.Wpf/
│   │   ├── Features/
│   │   │   ├── Profiles/
│   │   │   │   ├── ProfileListView.xaml
│   │   │   │   ├── ProfileListViewModel.cs
│   │   │   │   └── ProfileItemViewModel.cs
│   │   │   ├── CreateProfile/
│   │   │   ├── DeleteProfile/
│   │   │   └── SwitchProfile/
│   │   ├── Dialogs/
│   │   ├── Navigation/
│   │   └── App.xaml
│   │
│   └── CodexSwitcher.Bootstrapper/
│       ├── Program.cs
│       └── DependencyInjection.cs
│
├── tests/
│   ├── CodexSwitcher.Domain.Tests/
│   ├── CodexSwitcher.Application.Tests/
│   └── CodexSwitcher.Infrastructure.IntegrationTests/
│
└── spike/
    ├── New-CodexProfile.ps1
    ├── Start-CodexProfile.ps1
    ├── Switch-CodexProfile.ps1
    ├── Remove-CodexProfile.ps1
    └── tests/
        └── CodexProfile.Tests.ps1
```

---

## 15. 핵심 Domain 모델과 규칙

### 15.1 `CodexProfile`

예상 속성:

- `ProfileId`
- `ProfileName`
- `CodexHomePath`
- `CreatedAt`
- `LastUsedAt`

### 15.2 Domain 불변식

- 프로필 ID는 생성 후 변경할 수 없다.
- 프로필 이름은 비어 있을 수 없다.
- 프로필 이름은 정규화 후 중복될 수 없다.
- 프로필 저장 경로는 프로필 ID에 의해 결정하며 UI 입력을 직접 사용하지 않는다.
- 인증 토큰과 로그인 비밀번호는 Domain 모델에 포함하지 않는다.
- 삭제된 프로필은 다시 실행하거나 전환 대상으로 사용할 수 없다.

### 15.3 Application 정책

- 동일 프로필로의 전환은 멱등적으로 처리한다.
- 서로 다른 프로필의 Codex 인스턴스를 동시에 실행하지 않는다.
- 프로필 전환은 `종료 → 환경 적용 → 재실행`의 원자적 흐름으로 취급한다.
- 프로필 삭제는 확인 여부가 전달된 경우에만 실행한다.
- 실행 중인 프로필 삭제 시 먼저 안전 종료 정책을 적용한다.
- 실패 시 활성 프로필 상태를 성급하게 변경하지 않는다.

---

## 16. 주요 Use Case

| Use Case | 입력 | 출력 | 주요 책임 |
|---|---|---|---|
| `ListProfiles` | 없음 | 프로필 목록 | 정렬된 프로필 조회 |
| `CreateProfile` | 프로필 이름 | 생성 프로필 | 중복 검사, 저장소 생성, 권한 적용 |
| `RenameProfile` | 프로필 ID, 새 이름 | 변경 결과 | 이름 규칙 및 중복 검사 |
| `LaunchProfile` | 프로필 ID | 실행 결과 | 선택 환경으로 Codex 실행 |
| `SwitchProfile` | 대상 프로필 ID | 전환 결과 | 현재 실행 확인, 종료, 새 환경 실행 |
| `DeleteProfile` | 프로필 ID, 사용자 확인 | 삭제 결과 | 실행 상태 확인, 인증 데이터 포함 삭제 |
| `GetActiveProfile` | 없음 | 활성 프로필 | UI 표시용 상태 제공 |

### 16.1 `SwitchProfile` 흐름

```text
SwitchProfileCommand
        ↓
SwitchProfileUseCase
        ├── 대상 프로필 조회
        ├── 현재 활성 프로필 확인
        ├── 동일 프로필이면 No-op 또는 포커스
        ├── Codex 실행 여부 확인
        ├── 정상 종료 요청
        ├── 종료 확인
        ├── 대상 CODEX_HOME으로 실행
        ├── 실행 성공 확인
        └── 활성 프로필 상태 갱신
```

### 16.2 `DeleteProfile` 흐름

```text
Delete 버튼
   ↓
ViewModel이 확인 다이얼로그 요청
   ↓
사용자가 명시적으로 확인
   ↓
DeleteProfileUseCase
   ├── 프로필 조회
   ├── 현재 실행 여부 확인
   ├── 필요 시 안전 종료
   ├── 프로필 저장소 삭제
   ├── 메타데이터 삭제
   └── 결과 반환
```

확인 팝업은 Presentation 책임이며, 실제 삭제 정책과 순서는 Application 책임이다.

---

## 17. Application 포트

Application 계층이 필요한 기능을 인터페이스로 소유하고, Infrastructure가 구현한다.

```csharp
public interface IProfileRepository
{
    Task<IReadOnlyList<CodexProfile>> ListAsync(CancellationToken cancellationToken);
    Task<CodexProfile?> GetAsync(ProfileId id, CancellationToken cancellationToken);
    Task<bool> ExistsByNameAsync(ProfileName name, CancellationToken cancellationToken);
    Task SaveAsync(CodexProfile profile, CancellationToken cancellationToken);
    Task DeleteAsync(ProfileId id, CancellationToken cancellationToken);
}

public interface ICodexProcessGateway
{
    Task<CodexProcessState> GetStateAsync(CancellationToken cancellationToken);
    Task<CloseCodexResult> RequestCloseAsync(CancellationToken cancellationToken);
    Task ForceCloseAsync(CancellationToken cancellationToken);
}

public interface ICodexLauncher
{
    Task<LaunchCodexResult> LaunchAsync(
        string codexHomePath,
        CancellationToken cancellationToken);
}
```

인터페이스 이름은 기술적 구현보다 Application이 원하는 역할을 표현해야 한다.

---

## 18. MVVM 구성

### 18.1 View

- 프로필 목록 렌더링
- 버튼 및 입력 컨트롤 제공
- ViewModel 속성과 Command에 바인딩
- 순수 UI 이벤트 외 코드비하인드 최소화

### 18.2 ViewModel

예상 상태:

- `ObservableCollection<ProfileItemViewModel> Profiles`
- `ProfileItemViewModel? SelectedProfile`
- `bool IsBusy`
- `string? ErrorMessage`
- `string? ActiveProfileName`

예상 Command:

- `LoadProfilesCommand`
- `CreateProfileCommand`
- `LaunchProfileCommand`
- `SwitchProfileCommand`
- `RenameProfileCommand`
- `DeleteProfileCommand`
- `RetryCommand`

ViewModel의 책임:

- UI 입력을 Use Case 요청 모델로 변환
- 비동기 실행 상태 관리
- Command 활성화 조건 관리
- 결과를 화면 상태로 변환
- 다이얼로그 포트 호출

ViewModel이 하면 안 되는 일:

- `Directory.Delete` 직접 호출
- `Process.Start` 직접 호출
- `auth.json` 직접 수정
- 프로필 중복 규칙 직접 판단
- 전환 순서 직접 구현

### 18.3 Model 용어 구분

MVVM의 `Model`을 하나의 거대한 UI 모델로 만들지 않는다.

- Domain Model: 제품 규칙과 불변식
- Application DTO: Use Case 입력·출력
- Presentation Model: 화면 표시 전용 데이터

각 모델은 계층 경계에서 명시적으로 변환한다.

---

## 19. 로컬 데이터 구조

추천 경로:

```text
%LOCALAPPDATA%\CodexAccountSwitcher\
├── settings.json
├── profiles.json
├── profiles\
│   ├── {profile-id}\
│   │   ├── profile.json
│   │   └── codex-home\
│   │       ├── config.toml
│   │       ├── auth.json
│   │       ├── logs\
│   │       └── ...
│   └── {profile-id}\
│       └── ...
└── logs\
```

### 19.1 저장 원칙

- 사용자 입력 프로필 이름을 실제 폴더명으로 직접 사용하지 않는다.
- 폴더는 GUID 기반 프로필 ID로 생성한다.
- `profile.json`에는 표시 이름과 메타데이터만 저장한다.
- 인증 정보는 해당 프로필의 `codex-home` 내부에서 Codex가 직접 관리한다.
- 전환 도구는 인증 파일 내부 값을 읽을 필요가 없는 구조를 우선한다.

### 19.2 프로필 메타데이터 예시

```json
{
  "id": "5a194fe5-94cb-4a2e-9a62-50d91b8bda28",
  "name": "Work",
  "createdAt": "2026-06-18T10:00:00+09:00",
  "lastUsedAt": "2026-06-18T12:30:00+09:00"
}
```

토큰, 쿠키, 비밀번호, `auth.json` 복사본은 메타데이터에 포함하지 않는다.

---

## 20. 전환 상태 모델

계정 전환은 다음 상태를 가진다.

```text
Idle
  ↓
ValidatingProfile
  ↓
CheckingCodexProcess
  ↓
ClosingCurrentCodex   ── 실패 → Failed
  ↓
WaitingForExit        ── 시간 초과 → UserDecisionRequired
  ↓
LaunchingTarget       ── 실패 → Failed
  ↓
VerifyingLaunch
  ↓
Completed
```

### 20.1 상태별 UI 원칙

- 전환 중 중복 Command 실행을 막는다.
- 현재 진행 상태를 짧은 문구로 표시한다.
- 실패 시 재시도 또는 해결 방법을 제공한다.
- 실패한 경우 성공으로 표시하거나 활성 프로필을 변경하지 않는다.

---

## 21. 삭제 확인 UX

권장 확인 문구:

> **‘Work’ 프로필을 삭제하시겠습니까?**  
> 이 프로필의 Codex 로그인 정보와 로컬 상태가 함께 삭제됩니다. 이 작업은 되돌릴 수 없습니다.

버튼:

- `취소`
- `프로필 및 인증 데이터 삭제`

MVP에서는 텍스트 입력형 이중 확인이나 휴지통 복구는 제공하지 않는다.

---

## 22. 기술 검증 계획

GUI 개발 전에 PowerShell PoC로 다음 항목을 순서대로 검증한다.

### Spike-01. `CODEX_HOME` 인식 여부

- 서로 다른 프로필 경로로 Codex를 실행한다.
- 각 경로에 설정, 인증, 캐시 파일이 독립적으로 생성되는지 확인한다.
- Windows 데스크톱 앱 프로세스가 전달된 환경을 실제로 사용하는지 확인한다.

### Spike-02. 인증 세션 분리

- Personal 프로필로 로그인한다.
- Codex를 종료한다.
- Work 프로필로 로그인한다.
- 다시 Personal로 실행했을 때 재로그인 없이 원래 계정이 유지되는지 확인한다.

### Spike-03. 인증 저장 방식

- 프로필별 파일 기반 인증 저장 설정이 데스크톱 앱에 적용되는지 확인한다.
- Windows 공통 자격 증명 저장소 때문에 프로필 간 인증이 공유되지 않는지 확인한다.

### Spike-04. 웹 세션 비간섭

테스트 전후 다음을 확인한다.

- 기존 `chatgpt.com` 로그인 유지
- 기존 웹 로그인 계정 유지
- 열려 있던 채팅방 접근 가능
- 브라우저 로그아웃 발생 여부
- 사용자의 명시적 동작 없이 웹 계정이 변경되는지 여부

이 항목이 실패하면 현재 접근 방식으로는 MVP를 진행하지 않는다. 전용 브라우저 프로필 또는 로그인 격리 방식 등 대안을 별도 검토한다.

### Spike-05. 프로세스 제어

- Codex 설치 위치 탐색
- 실행 중 여부 확인
- 정상 종료 요청 가능 여부
- 종료 완료 감지
- 대상 환경으로 재실행
- 중복 실행 방지

### Spike-06. 앱 업데이트 영향

- Codex 업데이트 후 실행 경로 또는 패키지 식별자가 바뀌는지 확인한다.
- 바뀌더라도 `ICodexLocator` 구현만 교체할 수 있는지 검증한다.

---

## 23. PoC 합격 기준

다음 조건을 모두 만족해야 GUI MVP로 진행한다.

| 기준 | 합격 조건 |
|---|---|
| 환경 분리 | 프로필별 Codex 저장소가 독립적으로 생성됨 |
| 인증 분리 | Personal과 Work가 서로 다른 인증 상태를 유지함 |
| 재사용 | 앱 재실행 후에도 프로필별 로그인이 유지됨 |
| 웹 비간섭 | ChatGPT 웹 세션과 로그인 계정이 변하지 않음 |
| 전환 가능 | 자동 종료·재실행으로 계정 전환 가능 |
| 동시 실행 방지 | 서로 다른 프로필이 동시에 실행되지 않음 |
| 데이터 삭제 | 프로필 삭제 시 해당 인증 저장소 전체가 삭제됨 |

---

## 24. 테스트 전략

### 24.1 Domain 테스트

- 빈 프로필 이름 거부
- 이름 정규화
- 중복 프로필 판단에 필요한 값 객체 동작
- 프로필 ID 불변성

### 24.2 Application 테스트

Fake 또는 Mock 포트를 사용하여 실제 Codex와 Windows 없이 테스트한다.

- 프로필 생성 성공 및 중복 실패
- 저장소 생성 실패 시 메타데이터 롤백
- 동일 프로필 전환 시 불필요한 재시작 방지
- 다른 프로필 전환 시 종료 후 실행 순서 보장
- 종료 실패 시 새 프로필 실행 금지
- 실행 실패 시 활성 프로필 갱신 금지
- 사용자 확인 없이 삭제 금지
- 삭제 시 인증 저장소와 메타데이터 제거 순서 검증

### 24.3 Infrastructure 통합 테스트

- `%LOCALAPPDATA%` 테스트 디렉터리 생성·삭제
- NTFS 권한 설정
- JSON 원자적 저장
- 프로세스 실행 및 종료 어댑터
- Codex 설치 탐색 로직

### 24.4 수동 E2E 테스트

실제 Codex 및 실제 브라우저 세션이 필요한 항목은 수동 테스트 체크리스트로 관리한다.

- 최초 로그인
- 계정별 인증 유지
- 여러 차례 반복 전환
- Codex 비정상 종료 후 복구
- 웹 세션 비간섭
- 프로필 삭제 후 재실행 불가

---

## 25. 주요 리스크와 대응

| 리스크 | 영향 | 대응 |
|---|---|---|
| Codex 데스크톱 앱이 `CODEX_HOME`을 무시함 | 핵심 설계 실패 | PoC 최우선 검증, 실행 방식별 실험 |
| 인증이 Windows 공통 저장소에 저장됨 | 프로필 간 계정 공유 | 파일 기반 저장 강제 가능성 검증 |
| 로그인 과정이 웹 세션을 변경함 | 핵심 요구사항 실패 | MVP 중단, 전용 브라우저 프로필 등 대안 검토 |
| 앱 실행 중 파일 교체 충돌 | 인증 손상 가능 | 실행 중 파일 교체 금지, 재시작 방식 우선 |
| 정상 종료가 지원되지 않음 | 작업 손실 위험 | 종료 대기, 타임아웃, 사용자 확인 후 강제 종료 |
| Codex 업데이트로 실행 경로 변경 | 앱 실행 실패 | Locator 포트와 외부 어댑터로 변경 격리 |
| 인증 파일 로그 노출 | 보안 사고 | 구조화 로그 필터, 민감 데이터 읽기 금지 |
| 프로필 삭제 중 일부 실패 | 고아 데이터 발생 | 삭제 순서 및 재시도 가능한 상태 설계 |

---

## 26. MVP 화면 범위

### 26.1 메인 화면

- 앱 제목
- 프로필 목록
- 현재 활성 프로필 표시
- 프로필별 `실행` 또는 `전환` 버튼
- 프로필별 더보기 메뉴
- `새 프로필 추가` 버튼
- 진행 상태 및 오류 메시지

### 26.2 새 프로필 다이얼로그

- 프로필 이름 입력
- 중복 또는 유효성 오류 표시
- `취소`
- `생성 및 로그인`

### 26.3 삭제 확인 다이얼로그

- 프로필 이름
- 인증 데이터 포함 삭제 경고
- `취소`
- `프로필 및 인증 데이터 삭제`

### 26.4 종료 실패 다이얼로그

- Codex가 정상 종료되지 않았다는 안내
- 작업 손실 가능성 안내
- `취소`
- `강제 종료 후 전환`

---

## 27. 성공 지표 및 승인 기준

### 27.1 기능 승인 기준

- 두 개 이상의 프로필을 생성할 수 있다.
- 각 프로필로 최초 로그인할 수 있다.
- 재로그인 없이 프로필 간 반복 전환할 수 있다.
- 전환 전후 ChatGPT 웹 세션이 유지된다.
- 사용자가 Codex를 직접 종료하지 않아도 된다.
- 서로 다른 프로필을 동시에 실행하지 않는다.
- 프로필 삭제 시 인증 데이터까지 삭제된다.

### 27.2 사용성 승인 기준

- 사용자가 원하는 프로필을 1~2번 클릭으로 실행 또는 전환할 수 있다.
- 현재 활성 프로필을 즉시 식별할 수 있다.
- 오류 발생 시 다음 행동을 이해할 수 있다.

### 27.3 아키텍처 승인 기준

- Domain 프로젝트가 외부 프로젝트를 참조하지 않는다.
- Application 프로젝트가 WPF 및 Infrastructure를 참조하지 않는다.
- ViewModel이 파일시스템과 프로세스를 직접 호출하지 않는다.
- 핵심 Use Case가 실제 Codex와 Windows 없이 테스트된다.
- 프로필 생성, 전환, 삭제가 각각 명시적인 Use Case로 존재한다.
- Composition Root 외부에서 구체 Infrastructure 타입을 직접 조립하지 않는다.

---

## 28. 개발 단계

### Phase 0. 기술 검증

산출물:

- PowerShell 7 스크립트
- Pester 테스트
- 검증 결과 문서
- 웹 세션 비간섭 테스트 체크리스트

완료 조건:

- PoC 합격 기준 전부 충족

### Phase 1. WPF MVP

산출물:

- C# / .NET 10 / WPF 앱
- 프로필 생성, 목록, 실행, 전환, 삭제
- 로컬 JSON 저장
- 프로필별 Codex 저장소
- 자동 종료 및 재실행
- xUnit 테스트
- self-contained 배포 파일

### Phase 2. 안정화

후보 기능:

- 트레이 아이콘
- 최근 사용 프로필 빠른 전환
- 앱 시작 시 마지막 프로필 표시
- 진단 로그 내보내기
- Codex 설치 자동 재탐색
- 코드 서명

Phase 2 기능은 MVP 핵심 흐름이 안정화된 후에만 진행한다.

---

## 29. 오픈 기술 항목

다음 항목은 제품 요구사항이 아니라 PoC에서 답해야 할 기술 질문이다.

1. Codex Windows 데스크톱 앱은 프로세스별 `CODEX_HOME`을 인식하는가?
2. 인증 저장 방식을 프로필별 파일 저장으로 고정할 수 있는가?
3. Codex 로그인 절차가 기존 ChatGPT 웹 세션을 전혀 변경하지 않는가?
4. Windows 앱 패키지 실행 시 환경변수가 최종 Codex 프로세스에 전달되는가?
5. 정상 종료 요청과 종료 완료 감지가 안정적으로 가능한가?
6. Codex 실행 성공을 어떤 신호로 판단할 것인가?
7. 활성 프로필과 실제 Codex 로그인 계정의 일치 여부를 토큰 파싱 없이 확인할 수 있는가?

이 질문 중 1~3번은 Go/No-Go 조건이다.

---

## 30. 최종 요약

이 제품은 OpenAI 계정 관리자가 아니라 **Codex 실행 환경을 계정별로 분리하는 Windows 로컬 런처**이다.

사용자는 프로필을 선택하고 전환 버튼만 누른다. 내부적으로 재시작이 필요하면 도구가 처리한다. 최초 로그인은 기존 Codex 로그인 절차를 사용하며, 일반 브라우저의 ChatGPT 로그인 상태와 채팅 세션은 절대 변경되지 않아야 한다.

개발은 먼저 PowerShell PoC로 기술적 가능성을 검증하고, 성공한 실행 흐름만 C# / .NET 10 / WPF 앱으로 옮긴다. 아키텍처는 Clean Architecture의 의존성 규칙을 지키고, MVVM은 Presentation 계층의 구조로만 사용한다. 프로필 생성·전환·삭제는 각각 명시적인 Use Case로 구현하며, Codex 프로세스·파일시스템·Windows 권한은 교체 가능한 외부 어댑터로 둔다.

> 핵심 가치: ChatGPT 웹 작업을 끊지 않고, 원하는 Codex 계정으로 안전하게 전환한다.
