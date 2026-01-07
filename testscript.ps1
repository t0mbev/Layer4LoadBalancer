

try {

    Start-Process -FilePath ".\SimpleBackendService\bin\Debug\net10.0\SimpleBackendService" `
                  -ArgumentList "5001" `
                  -NoNewWindow:$false `
                  -Wait:$false
}
catch {
    Write-Error "Failed to start process: $_"
}

try {

    Start-Process -FilePath ".\SimpleBackendService\bin\Debug\net10.0\SimpleBackendService" `
                  -ArgumentList "5002" `
                  -NoNewWindow:$false `
                  -Wait:$false
}
catch {
    Write-Error "Failed to start process: $_"
}

try {

    Start-Process -FilePath ".\SimpleBackendService\bin\Debug\net10.0\SimpleBackendService" `
                  -ArgumentList "5003" `
                  -NoNewWindow:$false `
                  -Wait:$false
}
catch {
    Write-Error "Failed to start process: $_"
}