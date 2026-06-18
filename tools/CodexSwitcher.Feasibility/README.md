# CodexSwitcher.Feasibility

Codex 계정 전환 MVP의 핵심 가정을 확인하는 Windows 전용 실험 도구입니다.

이 도구는 인증 JSON의 내용을 해석하거나 출력하지 않습니다. 인증 상태는 바이트 그대로 다루고 `%LOCALAPPDATA%\CodexAccountSwitcher\Feasibility`에 Windows DPAPI 현재 사용자 범위로 암호화해 보관합니다.

## 안전 원칙

- `auth.json` 파일 방식이 확인되지 않으면 교체 명령을 중단합니다.
- Codex 프로세스가 실행 중이면 인증 파일을 변경하지 않습니다.
- 정상 종료만 요청하며 강제 종료는 수행하지 않습니다.
- `codex` 명령 별칭이 없어도 `Get-AppxPackage`와 `Get-StartApps`로 Store 앱을 찾습니다.
- Store 앱은 `resources\codex.exe`가 아니라 AUMID로 실행합니다.
- Codex 통합 터미널에서 실행된 `close`는 순환 대기를 막기 위해 거부합니다.
- 정상 `close`는 `WM_CLOSE` 후 Windows Restart Manager API를 순서대로 시도합니다.
- `close --force`는 작업 손실 위험을 확인한 사용자가 명시적으로 입력했을 때만 실행됩니다.
- 파일 교체 실패 시 직전 인증 상태의 암호화 보관본으로 복구를 시도합니다.
- 슬롯 일치는 인증 내용을 읽어 해석하지 않고 전체 바이트가 정확히 같은지만 비교합니다.

## 먼저 실행할 명령

```powershell
dotnet run --project .\tools\CodexSwitcher.Feasibility -- self-test
dotnet run --project .\tools\CodexSwitcher.Feasibility -- inspect
dotnet run --project .\tools\CodexSwitcher.Feasibility -- verify
```

`rollback-test`는 Codex가 완전히 종료된 상태에서만 실행되며, 일부러 손상된 임시 슬롯의 적용을 시도한 뒤 직전 인증 파일이 정확히 복구됐는지 확인합니다.

`close`, `capture`, `prepare-login`, `activate`, `rollback-test`, `restore-original`은 Codex 통합 터미널이 아니라 Windows Terminal 또는 시작 메뉴에서 연 외부 PowerShell에서 실행하세요.

두 계정 수동 검증 절차는 [`docs/FEASIBILITY_0.md`](../../docs/FEASIBILITY_0.md)를 따릅니다.
