// Minimal SSE-over-fetch helper (allows Authorization header).
// Parses lines like: event: snapshot\n data: {...}\n\n

export type StreamMessage = { event: string, data: any }

export async function startStream(url: string, token: string, onMessage: (m: StreamMessage) => void, signal: AbortSignal) {
  const res = await fetch(url, {
    headers: {
      'Authorization': `Bearer ${token}`,
      'Accept': 'text/event-stream'
    },
    signal
  })

  if(!res.ok || !res.body) throw new Error(`Stream failed: HTTP ${res.status}`)

  const reader = res.body.getReader()
  const decoder = new TextDecoder('utf-8')

  let buf = ''
  let currentEvent = 'message'

  while(true){
    const { done, value } = await reader.read()
    if(done) break
    buf += decoder.decode(value, { stream:true })

    // Process full events separated by blank line
    while(true){
      const idx = buf.indexOf('\n\n')
      if(idx === -1) break
      const chunk = buf.slice(0, idx)
      buf = buf.slice(idx + 2)

      const lines = chunk.split('\n').map(l => l.trimEnd())
      let dataLines: string[] = []
      currentEvent = 'message'
      for(const line of lines){
        if(line.startsWith('event:')) currentEvent = line.slice(6).trim()
        if(line.startsWith('data:')) dataLines.push(line.slice(5).trim())
      }
      const dataStr = dataLines.join('\n')
      if(dataStr){
        try{
          onMessage({ event: currentEvent, data: JSON.parse(dataStr) })
        }catch{
          onMessage({ event: currentEvent, data: dataStr })
        }
      }
    }
  }
}
