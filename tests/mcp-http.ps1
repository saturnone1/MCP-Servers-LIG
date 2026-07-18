if (-not ('System.Net.Http.HttpClient' -as [type])) {
    Add-Type -AssemblyName System.Net.Http
}

$script:McpSmokeHttpClient = [System.Net.Http.HttpClient]::new()
$script:McpSmokeHttpClient.Timeout = [TimeSpan]::FromSeconds(120)

function Invoke-McpHttpPost {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$Body,
        [string]$SessionId = ''
    )

    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $Uri)
    try {
        [void]$request.Headers.TryAddWithoutValidation('Accept', 'application/json, text/event-stream')
        if (-not [string]::IsNullOrWhiteSpace($SessionId)) {
            [void]$request.Headers.TryAddWithoutValidation('Mcp-Session-Id', $SessionId)
        }
        $request.Content = [System.Net.Http.StringContent]::new($Body, [Text.Encoding]::UTF8, 'application/json')

        $response = $script:McpSmokeHttpClient.SendAsync($request).GetAwaiter().GetResult()
        try {
            $content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if (-not $response.IsSuccessStatusCode) {
                throw "MCP HTTP request failed with status $([int]$response.StatusCode): $content"
            }

            $headers = @{}
            foreach ($header in $response.Headers) {
                $headers[$header.Key] = [string]::Join(', ', $header.Value)
            }
            foreach ($header in $response.Content.Headers) {
                $headers[$header.Key] = [string]::Join(', ', $header.Value)
            }

            return [pscustomobject]@{
                StatusCode = [int]$response.StatusCode
                Headers = $headers
                Content = $content
            }
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $request.Dispose()
    }
}
