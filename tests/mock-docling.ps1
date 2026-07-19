param([int]$Port = 42209)

$ErrorActionPreference = 'Stop'
$conversionBody = '{"status":"success","processing_time":0.01,"errors":[],"document":{"md_content":"# Test document\\n\\nThe first page contains alpha.\\n\\n## Table\\n\\nThe second page contains beta.","text_content":"The first page contains alpha. The second page contains beta.","json_content":{"name":"Test PDF","pages":{"1":{},"2":{}},"texts":[{"label":"section_header","text":"Test document","level":1,"prov":[{"page_no":1,"confidence":0.99}]},{"label":"text","text":"The first page contains alpha.","prov":[{"page_no":1,"confidence":0.96}]},{"label":"section_header","text":"Table","level":2,"prov":[{"page_no":2,"confidence":0.99}]},{"label":"text","text":"The second page contains beta.","prov":[{"page_no":2,"confidence":0.94}]}],"tables":[{"label":"table","prov":[{"page_no":2}],"data":{"table_cells":[{"start_row_offset_idx":0,"start_col_offset_idx":0,"text":"item"},{"start_row_offset_idx":0,"start_col_offset_idx":1,"text":"value"},{"start_row_offset_idx":1,"start_col_offset_idx":0,"text":"A"},{"start_row_offset_idx":1,"start_col_offset_idx":1,"text":"10"}]}}]}}}'
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()
try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        try {
            if ($context.Request.HttpMethod -eq 'GET' -and $context.Request.Url.AbsolutePath -eq '/health') {
                $body = '{"status":"ok"}'
                $context.Response.StatusCode = 200
            }
            elseif ($context.Request.HttpMethod -eq 'POST' -and $context.Request.Url.AbsolutePath -eq '/v1/convert/file/async') {
                $context.Request.InputStream.CopyTo([System.IO.Stream]::Null)
                $body = '{"task_id":"mock-task","task_status":"pending","task_position":1,"task_meta":null}'
                $context.Response.StatusCode = 200
            }
            elseif ($context.Request.HttpMethod -eq 'POST' -and $context.Request.Url.AbsolutePath -eq '/v1/convert/file') {
                $context.Request.InputStream.CopyTo([System.IO.Stream]::Null)
                $body = $conversionBody
                $context.Response.StatusCode = 200
            }
            elseif ($context.Request.HttpMethod -eq 'GET' -and $context.Request.Url.AbsolutePath -eq '/v1/status/poll/mock-task') {
                $body = '{"task_id":"mock-task","task_status":"success","task_position":0,"task_meta":null}'
                $context.Response.StatusCode = 200
            }
            elseif ($context.Request.HttpMethod -eq 'GET' -and $context.Request.Url.AbsolutePath -eq '/v1/result/mock-task') {
                $body = $conversionBody
                $context.Response.StatusCode = 200
            }
            else {
                $body = '{"error":"not found"}'
                $context.Response.StatusCode = 404
            }
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
            $context.Response.ContentType = 'application/json; charset=utf-8'
            $context.Response.ContentLength64 = $bytes.Length
            $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        }
        finally { $context.Response.Close() }
    }
}
finally { $listener.Close() }
