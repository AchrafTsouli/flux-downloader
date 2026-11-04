// Executed in the player's MAIN world. Return only bounded format metadata, never signed URLs or tokens.
export function readPlayerMetadata(expectedId) {
  try {
    const candidates = [];
    try { candidates.push(document.getElementById('movie_player')?.getPlayerResponse?.()); } catch {}
    candidates.push(window.ytInitialPlayerResponse);
    const response = candidates.find(value => value?.videoDetails?.videoId === expectedId);
    if (!response || response.playabilityStatus?.status !== 'OK' || response.microformat?.playerMicroformatRenderer?.liveBroadcastDetails?.isLiveNow) return null;
    const streaming = response.streamingData;
    if (!streaming || streaming.licenseInfos?.length) return null;
    const formats = [...(streaming.formats || []), ...(streaming.adaptiveFormats || [])].slice(0,300).filter(item => !item.drmFamilies?.length).map(item => ({
      id: String(item.itag), type: String(item.mimeType || '').slice(0,200), height:Number(item.height)||0, fps:Number(item.fps)||0,
      bitrate:Number(item.bitrate)||0, size:Number(item.contentLength)||0, hasAudio:!!item.audioQuality,
      defaultAudio:!!item.audioTrack?.audioIsDefault
    }));
    return {id:expectedId,title:String(response.videoDetails.title || 'Video').slice(0,200),formats};
  } catch { return null; }
}

export function playerCatalog(metadata, expectedId) {
  if (!metadata || metadata.id !== expectedId || !Array.isArray(metadata.formats)) return null;
  const parsed = metadata.formats.slice(0,300).flatMap(item=>{
    if (!/^\d{1,8}$/.test(item.id || '')) return [];
    const type=String(item.type || '');const mime=type.split(';')[0].trim();
    if(!['video/mp4','video/webm','audio/mp4','audio/webm'].includes(mime))return [];
    const codecs=type.match(/codecs="([^"]+)"/)?.[1].split(',').map(c=>c.trim()) || [];
    const audio=mime.startsWith('audio/');const hasAudio=audio||item.hasAudio||codecs.some(codec=>/^(mp4a|opus|vorbis)/.test(codec));
    const size=Number.isSafeInteger(item.size)&&item.size>0?item.size:null;
    return [{...item,id:String(item.id),audio,hasAudio,size,codec:codecs[0]||'',ext:mime.endsWith('webm')?'webm':audio?'m4a':'mp4'}];
  });
  const audio=parsed.filter(item=>item.audio).sort((a,b)=>Number(b.defaultAudio)-Number(a.defaultAudio)||b.bitrate-a.bitrate);
  const videos=parsed.filter(item=>!item.audio&&item.height>0&&item.height<=10000&&item.fps<=240).sort((a,b)=>b.height-a.height||b.fps-a.fps||b.bitrate-a.bitrate);
  const formats=[],seen=new Set();
  for(const video of videos){
    let formatId=video.id,ext=video.ext,totalBytes=video.size;
    if(!video.hasAudio){
      const sound=audio.find(item=>item.ext===(ext==='mp4'?'m4a':'webm'))||audio[0];if(!sound)continue;
      formatId+='+'+sound.id;
      totalBytes=totalBytes&&sound.size&&Number.isSafeInteger(totalBytes+sound.size)?totalBytes+sound.size:null;
      if(sound.ext!==(ext==='mp4'?'m4a':'webm'))ext='mkv';
    }
    const codec=/^avc1/.test(video.codec)?'H.264':/^av01/.test(video.codec)?'AV1':/^vp0?9/.test(video.codec)?'VP9':video.codec.split('.')[0].toUpperCase();
    const key=`${ext}:${video.height}:${video.fps}:${codec}`;if(seen.has(key))continue;seen.add(key);
    formats.push({formatId,extension:ext,totalBytes,approximate:false,label:`${ext.toUpperCase()} · ${video.height}p${video.fps>30?` · ${video.fps} fps`:''} · ${codec} · video + audio`});
    if(formats.length>=50)break;
  }
  return formats.length?{title:String(metadata.title||'Video').slice(0,200),formats,source:'player'}:null;
}
