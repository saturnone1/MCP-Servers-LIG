$ErrorActionPreference = 'Stop'

while ($null -ne ($line = [Console]::In.ReadLine())) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try {
        $request = $line | ConvertFrom-Json
    }
    catch {
        continue
    }

    if ($null -eq $request.id) {
        continue
    }

    $result = switch ($request.method) {
        'initialize' {
            @{
                protocolVersion = '2025-06-18'
                capabilities = @{ tools = @{} }
                serverInfo = @{ name = 'mock-matlab-mcp'; version = '1.0' }
            }
        }
        'tools/list' {
            @{
                tools = @(
                    @{
                        name = 'mock_echo'
                        description = 'Echo arguments for bridge smoke tests.'
                        inputSchema = @{
                            type = 'object'
                            properties = @{ text = @{ type = 'string' } }
                        }
                    }
                )
            }
        }
        'tools/call' {
            @{
                content = @(
                    @{
                        type = 'text'
                        text = "mock:$($request.params.name):$($request.params.arguments.text)"
                    }
                )
                isError = $false
            }
        }
        default {
            @{
                ok = $true
                method = $request.method
                params = $request.params
            }
        }
    }

    $response = @{
        jsonrpc = '2.0'
        id = $request.id
        result = $result
    } | ConvertTo-Json -Depth 20 -Compress

    [Console]::Out.WriteLine($response)
    [Console]::Out.Flush()
}
