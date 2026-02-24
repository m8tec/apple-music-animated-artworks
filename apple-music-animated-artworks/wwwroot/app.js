const ui = {
    form: document.getElementById('searchForm'),
    submitBtn: document.getElementById('submitBtn'),
    spinner: document.getElementById('loadingSpinner'),
    statusMessage: document.getElementById('statusMessage'),
    videoContainer: document.getElementById('videoContainer'),
    videoElement: document.getElementById('artworkVideo'),
    rawLink: document.getElementById('rawLink'),
    historyContainer: document.getElementById('historyContainer'),
    historyList: document.getElementById('historyList'),
    
    tabDetails: document.getElementById('tabDetails'),
    tabUrl: document.getElementById('tabUrl'),
    groupDetails: document.getElementById('groupDetails'),
    groupUrl: document.getElementById('groupUrl'),
    
    downloadBtn: document.getElementById('downloadMp4Btn'),
    downloadBtnText: document.getElementById('downloadBtnText'),
    
    artworkMetadata: document.getElementById('artworkMetadata'),
    metaAlbum: document.getElementById('metaAlbum'),
    metaArtist: document.getElementById('metaArtist'),
    cacheBadge: document.getElementById('cacheBadge')
};

let state = {
    currentMode: 'details',
    mainHls: null,
    historyHlsInstances: [],
    currentM3u8Url: null,
    currentAlbumName: null
};

const { FFmpeg } = window.FFmpegWASM;
let ffmpeg = null;

function setMode(mode) {
    state.currentMode = mode;
    const activeClass = "flex-1 py-2 text-sm font-medium rounded-lg transition-all bg-gradient-to-r from-pink-600 to-orange-500 text-white shadow-lg";
    const inactiveClass = "flex-1 py-2 text-sm font-medium rounded-lg transition-all text-gray-400 hover:text-white";

    if (mode === 'details') {
        ui.tabDetails.className = activeClass;
        ui.tabUrl.className = inactiveClass;
        ui.groupDetails.classList.remove('hidden');
        ui.groupUrl.classList.add('hidden');
    } else {
        ui.tabUrl.className = activeClass;
        ui.tabDetails.className = inactiveClass;
        ui.groupUrl.classList.remove('hidden');
        ui.groupDetails.classList.add('hidden');
    }
}

function showError(msg) {
    ui.statusMessage.textContent = msg;
    ui.statusMessage.className = "mt-4 text-center text-sm text-red-400";
    ui.statusMessage.classList.remove('hidden');
    ui.submitBtn.disabled = false;
    ui.spinner.classList.add('hidden');
}

function updateMetadataUI(data) {
    ui.artworkMetadata.classList.remove('hidden');
    ui.metaAlbum.textContent = data.album;
    ui.metaArtist.textContent = data.artist;

    if (data.isCached) {
        ui.cacheBadge.classList.remove('hidden');
        ui.cacheBadge.classList.add('flex');
    } else {
        ui.cacheBadge.classList.add('hidden');
        ui.cacheBadge.classList.remove('flex');
    }
}

function playVideo(url) {
    ui.statusMessage.classList.add('hidden');
    ui.videoContainer.classList.remove('hidden');
    ui.rawLink.href = url;
    ui.rawLink.textContent = url;
    ui.rawLink.classList.remove('hidden');

    if (Hls.isSupported()) {
        if (state.mainHls) state.mainHls.destroy();
        state.mainHls = new Hls();
        state.mainHls.loadSource(url);
        state.mainHls.attachMedia(ui.videoElement);
        state.mainHls.on(Hls.Events.MANIFEST_PARSED, () => {
            ui.videoElement.play().catch(e => console.log("Autoplay prevented:", e));
        });
    } else if (ui.videoElement.canPlayType('application/vnd.apple.mpegurl')) {
        ui.videoElement.src = url;
        ui.videoElement.addEventListener('loadedmetadata', () => {
            ui.videoElement.play().catch(e => console.log("Autoplay prevented:", e));
        });
    }
}

async function fetchSystemStatus() {
    const statusEl = document.getElementById('systemStatus');
    const statusPing = document.getElementById('statusPing');
    const statusDot = document.getElementById('statusDot');
    const statusText = document.getElementById('statusText');
    
    const statsContainer = document.getElementById('statsContainer');
    const statSearches = document.getElementById('statSearches');
    const statDownloads = document.getElementById('statDownloads');

    try {
        const res = await fetch('/api/v1/status');
        const data = await res.json();
        statusText.textContent = data.message;

        if (data.status === 'operational') {
            statusEl.className = "inline-flex items-center gap-2 px-3 py-1 rounded-full bg-green-500/10 text-green-400 text-xs font-medium border border-green-500/20 transition-colors";
            statusPing.className = "animate-ping absolute inline-flex h-full w-full rounded-full bg-green-400 opacity-75";
            statusDot.className = "relative inline-flex rounded-full h-2 w-2 bg-green-500";
            
            if (data.totalSearches !== undefined && data.totalDownloads !== undefined) {
                statSearches.textContent = data.totalSearches.toLocaleString();
                statDownloads.textContent = data.totalDownloads.toLocaleString();
                statsContainer.classList.remove('hidden');
                statsContainer.classList.add('flex');
            }
        } else {
            statusEl.className = "inline-flex items-center gap-2 px-3 py-1 rounded-full bg-yellow-500/10 text-yellow-400 text-xs font-medium border border-yellow-500/20 transition-colors";
            statusPing.className = "animate-ping absolute inline-flex h-full w-full rounded-full bg-yellow-400 opacity-75";
            statusDot.className = "relative inline-flex rounded-full h-2 w-2 bg-yellow-500";
        }
    } catch (e) {
        statusText.textContent = "Backend Offline";
        statusEl.className = "inline-flex items-center gap-2 px-3 py-1 rounded-full bg-red-500/10 text-red-400 text-xs font-medium border border-red-500/20 transition-colors";
        statusPing.classList.add('hidden');
        statusDot.className = "relative inline-flex rounded-full h-2 w-2 bg-red-500";
        statsContainer.classList.add('hidden');
        statsContainer.classList.remove('flex');
    }
}

async function fetchGlobalHistory() {
    try {
        const response = await fetch('/api/v1/artwork/history');
        if (!response.ok) return;

        const historyData = await response.json();
        if (historyData.length === 0) return;

        const fadeWrapper = document.getElementById('historyFadeWrapper');
        ui.historyContainer.classList.remove('hidden');
        ui.historyList.innerHTML = '';

        if (historyData.length >= 12) {
            fadeWrapper.classList.add('history-fade-wrapper');
        } else {
            fadeWrapper.classList.remove('history-fade-wrapper');
        }
        
        state.historyHlsInstances.forEach(hls => { if (hls) hls.destroy(); });
        state.historyHlsInstances = new Array(historyData.length).fill(null);
        
        const observer = new IntersectionObserver((entries) => {
            entries.forEach(entry => {
                const video = entry.target;
                const index = video.getAttribute('data-index');
                const url = video.getAttribute('data-url');

                if (entry.isIntersecting) {
                    if (!state.historyHlsInstances[index]) {
                        if (Hls.isSupported()) {
                            const thumbHls = new Hls({ capLevelToPlayerSize: true, autoStartLoad: true });
                            thumbHls.loadSource(url);
                            thumbHls.attachMedia(video);
                            thumbHls.on(Hls.Events.MANIFEST_PARSED, () => {
                                video.play().catch(() => {});
                            });
                            state.historyHlsInstances[index] = thumbHls;
                        } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
                            video.src = url;
                            video.addEventListener('loadedmetadata', () => {
                                video.play().catch(() => {});
                            });
                        }
                    } else {
                        video.play().catch(() => {});
                    }
                } else {
                    video.pause();
                }
            });
        }, {
            rootMargin: '50px'
        });

        historyData.forEach((item, index) => {
            const li = document.createElement('li');
            li.className = 'glass-panel p-2 rounded-lg history-item flex items-center gap-3 transition-colors cursor-pointer hover:bg-white/5';
            li.innerHTML = `
                <div class="w-12 h-12 flex-shrink-0 rounded bg-gray-800 border border-gray-700 overflow-hidden relative shadow-inner">
                    <video id="hist-vid-${index}" data-index="${index}" data-url="${item.url}" class="w-full h-full object-cover" loop muted playsinline></video>
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

                state.currentM3u8Url = item.url;
                state.currentAlbumName = item.album;

                updateMetadataUI({
                    album: item.album,
                    artist: item.artist,
                    isCached: true
                });

                playVideo(item.url);
                window.scrollTo({ top: 0, behavior: 'smooth' });
            };

            ui.historyList.appendChild(li);
            
            const thumbnailVideo = document.getElementById(`hist-vid-${index}`);
            observer.observe(thumbnailVideo);
        });
    } catch (error) {
        console.error("Failed to fetch history:", error);
    }
}

async function downloadArtworkAsMp4() {
    if (!state.currentM3u8Url) return;

    try {
        ui.downloadBtn.disabled = true;
        ui.downloadBtn.classList.add('opacity-50', 'cursor-not-allowed');
        
        if (!ffmpeg) {
            ui.downloadBtnText.textContent = "Loading Engine...";
            ffmpeg = new FFmpeg();
            ffmpeg.on('progress', ({ progress }) => {
                ui.downloadBtnText.textContent = `Converting... ${Math.round(progress * 100)}%`;
            });
            const baseUrl = window.location.origin + '/ffmpeg';
            await ffmpeg.load({
                coreURL: `${baseUrl}/ffmpeg-core.js`,
                wasmURL: `${baseUrl}/ffmpeg-core.wasm`
            });
        }
        
        ui.downloadBtnText.textContent = "Parsing playlist...";
        let res = await fetch(state.currentM3u8Url);
        let text = await res.text();
        let targetM3u8Url = state.currentM3u8Url;

        if (text.includes('#EXT-X-STREAM-INF')) {
            const lines = text.split('\n').map(l => l.trim()).filter(l => l);
            for (let i = 0; i < lines.length; i++) {
                if (lines[i].startsWith('#EXT-X-STREAM-INF')) {
                    let nextLine = lines[i + 1];
                    if (nextLine && !nextLine.startsWith('#')) {
                        targetM3u8Url = new URL(nextLine, state.currentM3u8Url).href;
                        break;
                    }
                }
            }
        }
        
        ui.downloadBtnText.textContent = "Fetching segments...";
        res = await fetch(targetM3u8Url);
        text = await res.text();

        const allSegments = text.split('\n')
            .map(l => l.trim())
            .filter(line => line.length > 0 && !line.startsWith('#'))
            .map(line => new URL(line, targetM3u8Url).href);
        const segments = [...new Set(allSegments)];
        
        let listFileContent = "";
        for (let i = 0; i < segments.length; i++) {
            ui.downloadBtnText.textContent = `Downloading chunk ${i+1}/${segments.length}...`;
            const segRes = await fetch(segments[i]);
            const segBuffer = await segRes.arrayBuffer();
            const fileName = `seg${i}.ts`;
            await ffmpeg.writeFile(fileName, new Uint8Array(segBuffer));
            listFileContent += `file '${fileName}'\n`;
        }

        await ffmpeg.writeFile('list.txt', listFileContent);
        
        ui.downloadBtnText.textContent = "Merging Video...";
        await ffmpeg.exec(['-f', 'concat', '-safe', '0', '-i', 'list.txt', '-c', 'copy', 'output.mp4']);
        
        const data = await ffmpeg.readFile('output.mp4');
        const videoBlob = new Blob([data.buffer], { type: 'video/mp4' });
        const downloadUrl = URL.createObjectURL(videoBlob);

        const a = document.createElement('a');
        a.href = downloadUrl;
        const safeName = state.currentAlbumName.replace(/[^a-z0-9]/gi, '_').toLowerCase();
        a.download = `${safeName}_artwork.mp4`;
        a.click();

        const storageKey = `downloaded_${state.currentM3u8Url}`;
        if (!localStorage.getItem(storageKey)) {
            try {
                await fetch('/api/v1/artwork/download', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ m3u8Url: state.currentM3u8Url })
                });
                localStorage.setItem(storageKey, 'true');
            } catch (e) {
                console.warn("Failed to report download stat:", e);
            }
        }
        
        // cleanup
        URL.revokeObjectURL(downloadUrl);
        await ffmpeg.deleteFile('output.mp4');
        await ffmpeg.deleteFile('list.txt');
        for (let i = 0; i < segments.length; i++) {
            await ffmpeg.deleteFile(`seg${i}.ts`);
        }

        ui.downloadBtnText.textContent = "Download Successful!";
        setTimeout(() => { ui.downloadBtnText.textContent = "Download as MP4"; }, 3000);

    } catch (e) {
        console.error("FFmpeg Error:", e);
        ui.downloadBtnText.textContent = "Error - Try again";
        setTimeout(() => { ui.downloadBtnText.textContent = "Download as MP4"; }, 3000);
    } finally {
        ui.downloadBtn.disabled = false;
        ui.downloadBtn.classList.remove('opacity-50', 'cursor-not-allowed');
    }
}

ui.tabDetails.onclick = () => setMode('details');
ui.tabUrl.onclick = () => setMode('url');
ui.downloadBtn.addEventListener('click', downloadArtworkAsMp4);

ui.form.addEventListener('submit', async (e) => {
    e.preventDefault();

    ui.statusMessage.classList.add('hidden');
    ui.videoContainer.classList.add('hidden');
    ui.rawLink.classList.add('hidden');
    ui.artworkMetadata.classList.add('hidden');
    ui.submitBtn.disabled = true;
    ui.spinner.classList.remove('hidden');

    let apiUrl = '';

    if (state.currentMode === 'details') {
        const artist = document.getElementById('artistInput').value.trim();
        const album = document.getElementById('albumInput').value.trim();
        const title = document.getElementById('titleInput').value.trim();

        if (!artist || !album) return showError("Please enter both Artist and Album.");

        const queryParams = { artist, album };
        if (title) queryParams.title = title;
        apiUrl = `/api/v1/artwork/search?${new URLSearchParams(queryParams)}`;
    } else {
        const url = document.getElementById('urlInput').value.trim();
        if (!url || !url.includes('music.apple.com')) return showError("Please enter a valid Apple Music URL.");
        apiUrl = `/api/v1/artwork/url?${new URLSearchParams({ url })}`;
    }

    try {
        const response = await fetch(apiUrl);
        if (!response.ok) {
            if (response.status === 404) throw new Error('No animated artwork found.');
            throw new Error('Server error occurred.');
        }

        const data = await response.json();
        
        state.currentM3u8Url = data.url;
        state.currentAlbumName = data.album;
        
        playVideo(data.url);
        updateMetadataUI(data);
        fetchGlobalHistory();

    } catch (error) {
        showError(error.message);
    } finally {
        ui.submitBtn.disabled = false;
        ui.spinner.classList.add('hidden');
    }
});


fetchGlobalHistory();
fetchSystemStatus();
setInterval(fetchSystemStatus, 30000);