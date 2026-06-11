# mcp-matlab vendor assets

이 폴더는 air-gap Windows 배포에 포함할 외부 실행 파일을 보관하는 자리입니다.

MathWorks 공식 MATLAB MCP Server binary를 인터넷이 되는 PC에서 내려받으려면:

```powershell
.\mcp-matlab\scripts\download-official-mcp.ps1
```

다운로드된 파일은 기본적으로 `mcp-matlab\vendor\official\`에 저장됩니다. 이후:

```powershell
.\mcp-matlab\scripts\publish-win.ps1
```

를 실행하면 publish 폴더의 `official\` 아래로 같이 복사됩니다. air-gap PC에서는 `run.ps1`이 `official\matlab-mcp*-win64.exe` 또는 `official\matlab-mcp*.exe`를 자동 탐지해 `MATLAB_MCP_CORE_SERVER_PATH`로 사용합니다.

주의:

- 공식 MathWorks MCP binary 자체는 Git에 커밋하지 않습니다.
- MATLAB 실행과 공식 MCP 기능은 대상 PC의 MATLAB 설치와 라이선스가 필요합니다.
