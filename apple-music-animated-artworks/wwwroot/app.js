const form = document.getElementById('searchForm');
const submitBtn = document.getElementById('submitBtn');
const spinner = document.getElementById('loadingSpinner');
const statusMessage = document.getElementById('statusMessage');
const videoContainer = document.getElementById('videoContainer');
const videoElement = document.getElementById('artworkVideo');
const rawLink = document.getElementById('rawLink');
const historyContainer = document.getElementById('historyContainer');
const historyList = document.getElementById('historyList');

const tabDetails = document.getElementById('tabDetails');
const tabUrl = document.getElementById('tabUrl');
const groupDetails = document.getElementById('groupDetails');
const groupUrl = document.getElementById('groupUrl');

let currentMode = 'details';
let mainHls;
let historyHlsInstances = [];

const { FFmpeg } = window.FFmpegWASM;
let ffmpeg = null;
let currentM3u8Url = null;
let currentAlbumName = null;

function setMode(mode) {
    currentMode = mode;
    if (mode === 'details') {
        tabDetails.className = "flex-1 py-2 text-sm font-medium rounded-lg transition-all bg-gradient-to-r from-pink-600 to-orange-500 text-white shadow-lg";
        tabUrl.className = "flex-1 py-2 text-sm font-medium rounded-lg transition-all text-gray-400 hover:text-white";
        groupDetails.classList.remove('hidden');
        groupUrl.classList.add('hidden');
    } else {
        tabUrl.className = "flex-1 py-2 text-sm font-medium rounded-lg transition-all bg-gradient-to-r from-pink-600 to-orange-500 text-white shadow-lg";
        tabDetails.className = "flex-1 py-2 text-sm font-medium rounded-lg transition-all text-gray-400 hover:text-white";
        groupUrl.classList.remove('hidden');
        groupDetails.classList.add('hidden');
    }
}

tabDetails.onclick = () => setMode('details');
tabUrl.onclick = () => setMode('url');

function playVideo(url) {
    statusMessage.classList.add('hidden');
    videoContainer.classList.remove('hidden');
    rawLink.href = url;
    rawLink.textContent = url;
    rawLink.classList.remove('hidden');

    if (Hls.isSupported()) {
        if (mainHls) mainHls.destroy();
        mainHls = new Hls();
        mainHls.loadSource(url);
        mainHls.attachMedia(videoElement);
        mainHls.on(Hls.Events.MANIFEST_PARSED, () => {
            videoElement.play().catch(e => console.log("Autoplay prevented:", e));
        });
    } else if (videoElement.canPlayType('application/vnd.apple.mpegurl')) {
        videoElement.src = url;
        videoElement.addEventListener('loadedmetadata', () => {
            videoElement.play().catch(e => console.log("Autoplay prevented:", e));
        });
    }
}

async function fetchGlobalHistory() {
    try {
        const response = await fetch('/api/v1/artwork/history');
        if (!response.ok) return;

        const historyData = await response.json();
        
        if (historyData.length > 0) {
            historyContainer.classList.remove('hidden');
            historyList.innerHTML = '';
            
            historyHlsInstances.forEach(hls => hls.destroy());
            historyHlsInstances = []; 
            
            historyData.forEach((item, index) => {
                const li = document.createElement('li');
                li.className = 'glass-panel p-2 rounded-lg history-item flex items-center gap-3 transition-colors';
                
                li.innerHTML = `
                    <div class="w-12 h-12 flex-shrink-0 rounded bg-gray-800 border border-gray-700 overflow-hidden relative shadow-inner">
                        <video id="hist-vid-${index}" class="w-full h-full object-cover" autoplay loop muted playsinline></video>
                    </div>
                    <div class="truncate flex-grow">
                        <p class="font-bold text-sm text-gray-200 truncate">${item.album}</p>
                        <p class="text-xs text-gray-400 truncate">${item.artist}</p>
                    </div>
                    <div class="text-xs text-gray-500 whitespace-nowrap ml-2 pr-2">
                        ${new Date(item.fetchedAt).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}
                    </div>
                `;

                li.onclick = () => {
                    document.getElementById('artistInput').value = item.artist;
                    document.getElementById('albumInput').value = item.album;
                    playVideo(item.url);
                    window.scrollTo({ top: 0, behavior: 'smooth' });
                };
                
                historyList.appendChild(li);

                const thumbnailVideo = document.getElementById(`hist-vid-${index}`);
                
                if (Hls.isSupported()) {
                    const thumbHls = new Hls({
                        capLevelToPlayerSize: true,
                        autoStartLoad: true
                    });
                    thumbHls.loadSource(item.url);
                    thumbHls.attachMedia(thumbnailVideo);
                    thumbHls.on(Hls.Events.MANIFEST_PARSED, () => {
                        thumbnailVideo.play().catch(e => console.log("Thumb Autoplay prevented:", e));
                    });
                    historyHlsInstances.push(thumbHls);
                } else if (thumbnailVideo.canPlayType('application/vnd.apple.mpegurl')) {
                    thumbnailVideo.src = item.url;
                    thumbnailVideo.addEventListener('loadedmetadata', () => {
                        thumbnailVideo.play().catch(e => console.log("Thumb Autoplay prevented:", e));
                    });
                }
            });
        }
    } catch (error) {
        console.error("Failed to fetch history:", error);
    }
}

form.addEventListener('submit', async (e) => {
    e.preventDefault();
    
    statusMessage.classList.add('hidden');
    videoContainer.classList.add('hidden');
    rawLink.classList.add('hidden');
    submitBtn.disabled = true;
    spinner.classList.remove('hidden');

    let apiUrl = '';
    
    if (currentMode === 'details') {
        const artist = document.getElementById('artistInput').value.trim();
        const album = document.getElementById('albumInput').value.trim();
        const title = document.getElementById('titleInput').value.trim();
        
        if (!artist || !album) {
            showError("Please enter both Artist and Album.");
            return;
        }

        const queryParams = { artist, album };
        if (title) {
            queryParams.title = title;
        }

        apiUrl = `/api/v1/artwork/search?${new URLSearchParams(queryParams)}`;
    } else {
        const url = document.getElementById('urlInput').value.trim();
        if (!url || !url.includes('music.apple.com')) {
            showError("Please enter a valid Apple Music URL.");
            return;
        }
        apiUrl = `/api/v1/artwork/url?${new URLSearchParams({ url })}`;
    }

    try {
        const response = await fetch(apiUrl);
        if (!response.ok) {
            if (response.status === 404) throw new Error('No animated artwork found.');
            throw new Error('Server error occurred.');
        }

        const data = await response.json();

        currentM3u8Url = data.url;
        currentAlbumName = data.album;
        
        playVideo(data.url);

        document.getElementById('artworkMetadata').classList.remove('hidden');
        document.getElementById('metaAlbum').textContent = data.album;
        document.getElementById('metaArtist').textContent = data.artist;
        
        const cacheBadge = document.getElementById('cacheBadge');
        if (data.isCached) {
            cacheBadge.classList.remove('hidden');
            cacheBadge.classList.add('flex');
        } else {
            cacheBadge.classList.add('hidden');
            cacheBadge.classList.remove('flex');
        }
        
        fetchGlobalHistory();
    } catch (error) {
        showError(error.message);
    } finally {
        submitBtn.disabled = false;
        spinner.classList.add('hidden');
    }
});

async function fetchSystemStatus() {
    try {
        const res = await fetch('/api/v1/status');
        const data = await res.json();

        const statusEl = document.getElementById('systemStatus');
        const statusPing = document.getElementById('statusPing');
        const statusDot = document.getElementById('statusDot');
        const statusText = document.getElementById('statusText');

        statusText.textContent = data.message;

        if (data.status === 'operational') {
            statusEl.className = "inline-flex items-center gap-2 px-3 py-1 rounded-full bg-green-500/10 text-green-400 text-xs font-medium border border-green-500/20 transition-colors";
            statusPing.className = "animate-ping absolute inline-flex h-full w-full rounded-full bg-green-400 opacity-75";
            statusDot.className = "relative inline-flex rounded-full h-2 w-2 bg-green-500";
        } else {
            statusEl.className = "inline-flex items-center gap-2 px-3 py-1 rounded-full bg-yellow-500/10 text-yellow-400 text-xs font-medium border border-yellow-500/20 transition-colors";
            statusPing.className = "animate-ping absolute inline-flex h-full w-full rounded-full bg-yellow-400 opacity-75";
            statusDot.className = "relative inline-flex rounded-full h-2 w-2 bg-yellow-500";
        }
    } catch (e) {
        const statusEl = document.getElementById('systemStatus');
        const statusPing = document.getElementById('statusPing');
        const statusDot = document.getElementById('statusDot');
        const statusText = document.getElementById('statusText');

        statusText.textContent = "Backend Offline";
        statusEl.className = "inline-flex items-center gap-2 px-3 py-1 rounded-full bg-red-500/10 text-red-400 text-xs font-medium border border-red-500/20 transition-colors";
        statusPing.classList.add('hidden');
        statusDot.className = "relative inline-flex rounded-full h-2 w-2 bg-red-500";
    }
}

function showError(msg) {
    statusMessage.textContent = msg;
    statusMessage.className = "mt-4 text-center text-sm text-red-400";
    statusMessage.classList.remove('hidden');
    submitBtn.disabled = false;
    spinner.classList.add('hidden');
}

document.getElementById('downloadMp4Btn').addEventListener('click', async () => {
    if (!currentM3u8Url) return;

    const btnText = document.getElementById('downloadBtnText');
    const btn = document.getElementById('downloadMp4Btn');

    try {
        btn.disabled = true;
        btn.classList.add('opacity-50', 'cursor-not-allowed');
        
        if (!ffmpeg) {
            btnText.textContent = "Loading Engine...";
            ffmpeg = new FFmpeg();

            ffmpeg.on('progress', ({ progress }) => {
                btnText.textContent = `Converting... ${Math.round(progress * 100)}%`;
            });

            const baseUrl = window.location.origin + '/ffmpeg';

            await ffmpeg.load({
                coreURL: `${baseUrl}/ffmpeg-core.js`,
                wasmURL: `${baseUrl}/ffmpeg-core.wasm`
            });
        }
        
        btnText.textContent = "Parsing playlist...";
        let res = await fetch(currentM3u8Url);
        let text = await res.text();

        let targetM3u8Url = currentM3u8Url;
        
        if (text.includes('#EXT-X-STREAM-INF')) {
            const lines = text.split('\n').map(l => l.trim()).filter(l => l);
            for (let i = 0; i < lines.length; i++) {
                if (lines[i].startsWith('#EXT-X-STREAM-INF')) {
                    let nextLine = lines[i + 1];
                    if (nextLine && !nextLine.startsWith('#')) {
                        targetM3u8Url = new URL(nextLine, currentM3u8Url).href;
                        break;
                    }
                }
            }
        }
        
        btnText.textContent = "Fetching segments...";
        res = await fetch(targetM3u8Url);
        text = await res.text();
        
        const allSegments = text.split('\n')
            .map(l => l.trim())
            .filter(line => line.length > 0 && !line.startsWith('#'))
            .map(line => new URL(line, targetM3u8Url).href);
        
        const segments = [...new Set(allSegments)];
        
        let listFileContent = "";
        for (let i = 0; i < segments.length; i++) {
            btnText.textContent = `Downloading chunk ${i+1}/${segments.length}...`;
            const segRes = await fetch(segments[i]);
            const segBuffer = await segRes.arrayBuffer();
            const fileName = `seg${i}.ts`;
            await ffmpeg.writeFile(fileName, new Uint8Array(segBuffer));
            listFileContent += `file '${fileName}'\n`;
        }
        
        await ffmpeg.writeFile('list.txt', listFileContent);
        
        btnText.textContent = "Merging Video...";
        await ffmpeg.exec(['-f', 'concat', '-safe', '0', '-i', 'list.txt', '-c', 'copy', 'output.mp4']);
        
        const data = await ffmpeg.readFile('output.mp4');
        const videoBlob = new Blob([data.buffer], { type: 'video/mp4' });
        const downloadUrl = URL.createObjectURL(videoBlob);

        const a = document.createElement('a');
        a.href = downloadUrl;
        const safeName = currentAlbumName.replace(/[^a-z0-9]/gi, '_').toLowerCase();
        a.download = `${safeName}_artwork.mp4`;
        a.click();
        
        URL.revokeObjectURL(downloadUrl);
        await ffmpeg.deleteFile('output.mp4');
        await ffmpeg.deleteFile('list.txt');

        btnText.textContent = "Download Successful!";
        setTimeout(() => { btnText.textContent = "Download as MP4"; }, 3000);

    } catch (e) {
        console.error("FFmpeg Error:", e);
        btnText.textContent = "Error - Try again";
        setTimeout(() => { btnText.textContent = "Download as MP4"; }, 3000);
    } finally {
        btn.disabled = false;
        btn.classList.remove('opacity-50', 'cursor-not-allowed');
    }
});

fetchGlobalHistory();

fetchSystemStatus();
setInterval(fetchSystemStatus, 30000);
