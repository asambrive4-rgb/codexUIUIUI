# 0단계 기술 가능성 검증 기록

> 상태: 완료 — 1단계 진행 조건 통과  
> 대상: Windows 10/11 x64, Microsoft Store형 Codex  
> 검증 완료일: 2026년 6월 18일

## 현재 설치 탐지 결과

2026년 6월 18일 현재 시험 PC에서 다음을 확인했다.

| 항목 | 결과 |
|---|---|
| `Get-Command codex` | 명령 별칭 없음 |
| Store 패키지 | `OpenAI.Codex` `26.611.8604.0` |
| Package Family Name | `OpenAI.Codex_2p2nqsd0c76g0` |
| AUMID | `OpenAI.Codex_2p2nqsd0c76g0!App` |
| 도구의 Store 탐지 | 성공 |
| `auth.json` 관찰 | 성공, 저장 방식 설정은 명시되지 않음 |

따라서 설치 탐지는 `codex` 명령 존재 여부에 의존하지 않고 `Get-AppxPackage`와 `Get-StartApps`를 우선 사용한다.

## 구현된 조사 도구

`tools/CodexSwitcher.Feasibility`에 다음 기능을 구현했다.

| 명령 | 역할 |
|---|---|
| `inspect` | Store 설치, AUMID, 인증 저장 방식, Codex 프로세스와 `.codex` 파일 변경 조사 |
| `capture <slot>` | 종료 상태에서 현재 `auth.json`을 DPAPI로 암호화해 보관 |
| `prepare-login` | 최초 인증과 복구본을 보관하고 새 로그인 상태 준비 |
| `activate <slot>` | 대상 슬롯을 원자적으로 적용하고 Store AUMID로 Codex 실행 |
| `restore-original` | 실험 전 최초 인증 상태 복구 |
| `verify` | 설치·인증 방식·슬롯 일치·프로세스 상태 표시 |
| `rollback-test` | 손상된 임시 슬롯을 적용해 실패 후 직전 인증 복구 확인 |
| `launch` | Store AUMID로 Codex 실행 |
| `close` | 정상 종료 요청 후 최대 10초 대기 |
| `close --force` | 정상 종료 실패 후 사용자가 명시적으로 승인한 강제 종료 |
| `self-test` | 실제 인증을 사용하지 않는 DPAPI·교체·복구·AUMID 테스트 |

도구는 이메일, 토큰, 인증 JSON 내용과 인증 해시를 출력하거나 로그로 남기지 않는다.

## 수동 검증 준비

1. 중요한 Codex 작업을 저장한다.
2. 일반 브라우저에서 ChatGPT 페이지 하나를 열어 로그인 계정과 채팅이 유지되는지 확인할 준비를 한다.
3. 서로 다른 시험용 Codex 계정 두 개를 준비한다.
4. Windows Terminal 또는 시작 메뉴에서 별도의 PowerShell을 연다.
5. Codex 통합 터미널은 Codex 종료와 순환 대기를 만들 수 있으므로 사용하지 않는다.
6. 아래 명령은 외부 PowerShell의 저장소 루트에서 실행한다.

```powershell
dotnet run --project .\tools\CodexSwitcher.Feasibility -- self-test
dotnet run --project .\tools\CodexSwitcher.Feasibility -- inspect
```

`inspect`가 파일 인증 교체를 지원한다고 표시하지 않으면 즉시 중단한다.

## 두 계정 검증 절차

현재 로그인된 계정을 `account-a`, 새 계정을 `account-b`로 기록한다.

```powershell
dotnet run --project .\tools\CodexSwitcher.Feasibility -- close
dotnet run --project .\tools\CodexSwitcher.Feasibility -- capture account-a
dotnet run --project .\tools\CodexSwitcher.Feasibility -- prepare-login
```

정상 종료가 실패하면 작업 손실 가능성을 먼저 확인한다. 계속해야 할 때만 다음 명령을 직접 실행한다.

```powershell
dotnet run --project .\tools\CodexSwitcher.Feasibility -- close --force
```

열린 Codex에서 `account-b` 로그인을 완료한 뒤:

```powershell
dotnet run --project .\tools\CodexSwitcher.Feasibility -- close
dotnet run --project .\tools\CodexSwitcher.Feasibility -- capture account-b
dotnet run --project .\tools\CodexSwitcher.Feasibility -- verify
dotnet run --project .\tools\CodexSwitcher.Feasibility -- rollback-test
```

다음 왕복을 최소 5회 반복한다.

```powershell
dotnet run --project .\tools\CodexSwitcher.Feasibility -- activate account-a
# Codex에서 계정과 공통 작업 상태를 확인한다.
dotnet run --project .\tools\CodexSwitcher.Feasibility -- close
dotnet run --project .\tools\CodexSwitcher.Feasibility -- capture account-a

dotnet run --project .\tools\CodexSwitcher.Feasibility -- activate account-b
# Codex에서 계정과 공통 작업 상태를 확인한다.
dotnet run --project .\tools\CodexSwitcher.Feasibility -- close
dotnet run --project .\tools\CodexSwitcher.Feasibility -- capture account-b
```

실험을 끝내거나 문제가 생기면 Codex를 종료한 뒤 최초 상태를 복구한다.

```powershell
dotnet run --project .\tools\CodexSwitcher.Feasibility -- restore-original
```

## 수동 확인표

- [x] 인증 관련 변경이 `auth.json`에 한정되는지 로그인 전후 `inspect` 결과로 확인
- [x] 두 계정이 매번 다시 로그인하지 않고 최소 5회 왕복
- [x] Codex가 토큰을 갱신한 뒤 다시 `capture`해도 이후 전환 성공
- [x] 전환 후 기존 스레드, 프로젝트 연결과 설정 유지
- [x] 브라우저 ChatGPT 로그인 계정과 열린 채팅 유지
- [x] 정상 종료 실패 후 사용자가 승인한 `close --force`로 종료 및 전환
- [x] `rollback-test`가 손상된 시험 슬롯 적용 실패 후 기존 인증을 복구
- [x] `verify`가 현재 인증과 정확히 일치하는 슬롯만 표시
- [x] Codex 밖에서 로그아웃하거나 다른 계정으로 로그인한 경우 `verify`가 슬롯을 추측하지 않고 `일치 없음` 표시

## 확인된 제한사항

시험한 Codex `26.611.8604.0`은 `WM_CLOSE`와 Windows Restart Manager를 통한 정상 종료 요청에 10초 안에 응답하지 않았다. 왕복 전환 시험은 사용자가 작업 손실 가능성을 확인한 뒤 `close --force`를 명시적으로 실행하는 방식으로 완료했다.

후속 전환 UI에서는 정상 종료 실패를 성공으로 처리하지 않고 다음 두 선택을 제공해야 한다.

- 취소
- 작업 손실 경고 확인 후 강제 종료 및 전환

## 1단계 진행 조건

위 확인표와 강제 종료 제한사항을 확인했으며 WPF 솔루션 구현을 진행한다.

특히 다음 중 하나라도 실패하면 1단계를 보류한다.

- `auth.json` 외의 자격 증명 저장소 교체가 필요함
- 인증 내용을 해석하지 않고 실제 활성 프로필을 신뢰할 수 있게 판별할 수 없음
- 공통 Codex 작업 데이터가 계정 전환 과정에서 손상되거나 분리됨
- 브라우저 ChatGPT 세션이 변경됨
- 실패 후 이전 인증 상태를 복구할 수 없음

## 공식 근거

- [Codex 인증](https://developers.openai.com/codex/auth)
- [Codex Windows 앱](https://developers.openai.com/codex/app/windows)
- [Codex 환경 변수](https://developers.openai.com/codex/environment-variables)
