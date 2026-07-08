param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('prometheus', 'gitlab', 'jira', 'confluence', 'loki')]
    [string]$Kind,

    [Parameter(Mandatory = $true)]
    [int]$Port
)

$ErrorActionPreference = 'Stop'

function Get-MockResponse([string]$Kind, [string]$Path) {
    $pathOnly = ($Path -split '\?')[0]
    if ($Kind -eq 'confluence' -and $pathOnly.StartsWith('/confluence/', [StringComparison]::OrdinalIgnoreCase)) {
        $pathOnly = $pathOnly.Substring('/confluence'.Length)
    }
    switch ($Kind) {
        'prometheus' {
            if ($pathOnly -eq '/-/ready') { return @{ Type = 'text/plain'; Body = 'Ready' } }
            if ($pathOnly -eq '/api/v1/labels') { return @{ Type = 'application/json'; Body = '{"status":"success","data":["job","instance"]}' } }
            if ($pathOnly -eq '/api/v1/query') { return @{ Type = 'application/json'; Body = '{"status":"success","data":{"resultType":"vector","result":[{"metric":{"job":"smoke"},"value":[0,"1"]}]}}' } }
            return @{ Type = 'application/json'; Body = '{"status":"success","data":{}}' }
        }
        'gitlab' {
            if ($pathOnly -eq '/api/v4/projects') { return @{ Type = 'application/json'; Body = '[{"id":1,"name":"smoke","path_with_namespace":"group/smoke","web_url":"http://gitlab.local/group/smoke"}]' } }
            if ($pathOnly -match '^/api/v4/projects/') { return @{ Type = 'application/json'; Body = '{"id":1,"name":"smoke","path_with_namespace":"group/smoke"}' } }
            return @{ Type = 'application/json'; Body = '{}' }
        }
        'jira' {
            if ($pathOnly -eq '/rest/api/3/project/search') { return @{ Type = 'application/json'; Body = '{"values":[{"id":"10000","key":"SMK","name":"Smoke"}]}' } }
            if ($pathOnly -eq '/rest/api/3/search') { return @{ Type = 'application/json'; Body = '{"issues":[{"key":"SMK-1","fields":{"summary":"Smoke issue"}}]}' } }
            if ($pathOnly -match '^/rest/api/3/issue/') { return @{ Type = 'application/json'; Body = '{"key":"SMK-1","fields":{"summary":"Smoke issue"}}' } }
            return @{ Type = 'application/json'; Body = '{}' }
        }
        'confluence' {
            if ($pathOnly -eq '/rest/api/settings/systemInfo') { return @{ Type = 'application/json'; Body = '{"version":"9.2.9","buildNumber":"9209","baseUrl":"http://mock/confluence"}' } }
            if ($pathOnly -eq '/rest/troubleshooting/1.0/pre-upgrade/info') { return @{ Type = 'application/json'; Body = '{"version":"6.15.8","buildNumber":"6158"}' } }
            if ($pathOnly -eq '/rest/api/space') { return @{ Type = 'application/json'; Body = '{"results":[{"id":1,"key":"SMK","name":"Smoke Space","type":"global","status":"current"}],"start":0,"limit":25,"size":1}' } }
            if ($pathOnly -eq '/rest/api/space/SMK') { return @{ Type = 'application/json'; Body = '{"id":1,"key":"SMK","name":"Smoke Space","type":"global","status":"current"}' } }
            if ($pathOnly -eq '/rest/api/content') { return @{ Type = 'application/json'; Body = '{"results":[{"id":"12345","type":"page","title":"Smoke Page","status":"current"}],"start":0,"limit":25,"size":1}' } }
            if ($pathOnly -eq '/rest/api/content/search') { return @{ Type = 'application/json'; Body = '{"results":[{"id":"12345","type":"page","title":"Smoke Page","status":"current"}],"start":0,"limit":25,"size":1}' } }
            if ($pathOnly -eq '/rest/api/user/current') { return @{ Type = 'application/json'; Body = '{"type":"known","username":"smoke","displayName":"Smoke User"}' } }
            if ($pathOnly -match '^/rest/api/content/[^/]+/child/page$') { return @{ Type = 'application/json'; Body = '{"results":[{"id":"23456","type":"page","title":"Child Page"}],"start":0,"limit":25,"size":1}' } }
            if ($pathOnly -match '^/rest/api/content/') { return @{ Type = 'application/json'; Body = '{"id":"12345","type":"page","title":"Smoke Page","version":{"number":1},"body":{"storage":{"value":"<p>Smoke</p>","representation":"storage"}}}' } }
            return @{ Type = 'application/json'; Body = '{}' }
        }
        'loki' {
            if ($pathOnly -eq '/ready') { return @{ Type = 'text/plain'; Body = 'ready' } }
            if ($pathOnly -eq '/loki/api/v1/labels') { return @{ Type = 'application/json'; Body = '{"status":"success","data":["job","namespace","pod"]}' } }
            if ($pathOnly -eq '/loki/api/v1/query_range') { return @{ Type = 'application/json'; Body = '{"status":"success","data":{"resultType":"streams","result":[{"stream":{"job":"smoke"},"values":[["0","smoke log"]]}]}}' } }
            if ($pathOnly -eq '/loki/api/v1/query') { return @{ Type = 'application/json'; Body = '{"status":"success","data":{"resultType":"streams","result":[]}}' } }
            return @{ Type = 'application/json'; Body = '{"status":"success","data":{}}' }
        }
    }
}

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, $Port)
$listener.Start()

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $buffer = New-Object byte[] 8192
            $builder = [System.Text.StringBuilder]::new()
            do {
                $read = $stream.Read($buffer, 0, $buffer.Length)
                if ($read -le 0) { break }
                [void]$builder.Append([System.Text.Encoding]::ASCII.GetString($buffer, 0, $read))
            } while ($builder.ToString() -notmatch "`r?`n`r?`n")

            $requestLine = ($builder.ToString() -split "`r?`n" | Select-Object -First 1)
            $path = '/'
            if ($requestLine -match '^\S+\s+(\S+)') {
                $path = $Matches[1]
            }

            try {
                $mock = Get-MockResponse $Kind $path
                $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($mock.Body)
                $header = "HTTP/1.1 200 OK`r`nContent-Type: $($mock.Type); charset=utf-8`r`nContent-Length: $($bodyBytes.Length)`r`nConnection: close`r`n`r`n"
                $headerBytes = [System.Text.Encoding]::ASCII.GetBytes($header)
                $stream.Write($headerBytes, 0, $headerBytes.Length)
                $stream.Write($bodyBytes, 0, $bodyBytes.Length)
            }
            catch {
                # Readiness probes and curl timeouts may close early; keep the mock server alive.
            }
        }
        finally {
            $client.Close()
        }
    }
}
finally {
    $listener.Stop()
}
