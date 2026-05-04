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
    downloadMp4Btn: document.getElementById('downloadMp4Btn'),
    downloadMp4BtnText: document.getElementById('downloadMp4BtnText'),
    downloadWebpBtn: document.getElementById('downloadWebpBtn'),
    downloadWebpBtnText: document.getElementById('downloadWebpBtnText'),
    resolutionSelect: document.getElementById('resolutionSelect'),
    webpQuality: document.getElementById('webpQuality'),
    webpQualityValue: document.getElementById('webpQualityValue'),
    artworkMetadata: document.getElementById('artworkMetadata'),
    metaAlbum: document.getElementById('metaAlbum'),
    metaArtist: document.getElementById('metaArtist'),
    cacheBadge: document.getElementById('cacheBadge'),
    variantSelector: document.getElementById('variantSelector'),
    variantSquareBtn: document.getElementById('variantSquareBtn'),
    variantTallBtn: document.getElementById('variantTallBtn'),
    coverVariantSelected: document.getElementById('coverVariantSelected'),
    selectedVariantBadge: document.getElementById('selectedVariantBadge')
};

let state = {
    currentMode: 'details',
    mainHls: null,
    historyHlsInstances: [],
    currentM3u8Url: null,
    currentAlbumName: null,
    currentArtworkVariants: {
        square: null,
        tall: null
    },
    selectedArtworkVariant: 'square',
    resolutionVariants: [],
    resolutionLoadToken: 0
};

const { FFmpeg } = window.FFmpegWASM;
let ffmpeg = null;
let activeProgressLabel = null;

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

function getArtworkVariantLabel(variant) {
    return variant === 'tall' ? 'Tall Cover' : 'Square Cover';
}

function getArtworkVariantShortLabel(variant) {
    return variant === 'tall' ? 'Tall' : 'Square';
}

function updateVariantBadge() {
    if (ui.coverVariantSelected) {
        ui.coverVariantSelected.textContent = getArtworkVariantShortLabel(state.selectedArtworkVariant);
    }

    if (ui.selectedVariantBadge) {
        ui.selectedVariantBadge.textContent = getArtworkVariantLabel(state.selectedArtworkVariant);
    }
}

function updatePreviewAspect(variant) {
    if (!ui.videoContainer) {
        return;
    }

    const isTall = variant === 'tall';
    ui.videoContainer.classList.toggle('cover-frame-square', !isTall);
    ui.videoContainer.classList.toggle('cover-frame-tall', isTall);
}

function updateVariantSelector() {
    if (!ui.variantSelector) {
        return;
    }

    const hasSquare = Boolean(state.currentArtworkVariants.square);
    const hasTall = Boolean(state.currentArtworkVariants.tall);
    const canSwitch = hasSquare && hasTall;

    ui.variantSelector.classList.toggle('hidden', !canSwitch);

    const activeButtonClass = 'variant-option variant-option-active rounded-lg px-3 py-2 text-left is-active';
    const inactiveButtonClass = 'variant-option variant-option-inactive rounded-lg px-3 py-2 text-left';
    const disabledButtonClass = 'variant-option variant-option-inactive rounded-lg px-3 py-2 text-left opacity-45 cursor-not-allowed';

    if (ui.variantSquareBtn) {
        ui.variantSquareBtn.disabled = !hasSquare;
        ui.variantSquareBtn.className = hasSquare
            ? (state.selectedArtworkVariant === 'square' ? activeButtonClass : inactiveButtonClass)
            : disabledButtonClass;
    }

    if (ui.variantTallBtn) {
        ui.variantTallBtn.disabled = !hasTall;
        ui.variantTallBtn.className = hasTall
            ? (state.selectedArtworkVariant === 'tall' ? activeButtonClass : inactiveButtonClass)
            : disabledButtonClass;
    }

    updateVariantBadge();
}

async function selectArtworkVariant(variant, playPreview = true) {
    const preferredVariant = state.currentArtworkVariants[variant] ? variant : (state.currentArtworkVariants.square ? 'square' : state.currentArtworkVariants.tall ? 'tall' : null);

    if (!preferredVariant) {
        return null;
    }

    const selectedUrl = state.currentArtworkVariants[preferredVariant];

    if (state.selectedArtworkVariant === preferredVariant && state.currentM3u8Url === selectedUrl) {
        updateVariantSelector();
        updatePreviewAspect(preferredVariant);

        if (playPreview) {
            playVideo(selectedUrl);
        }

        await loadResolutionOptions(selectedUrl);
        return selectedUrl;
    }

    state.selectedArtworkVariant = preferredVariant;
    state.currentM3u8Url = selectedUrl;

    updateVariantSelector();
    updatePreviewAspect(preferredVariant);

    if (playPreview) {
        playVideo(selectedUrl);
    }

    await loadResolutionOptions(selectedUrl);
    return selectedUrl;
}

async function applyArtworkData(data, isCached = false) {
    state.currentArtworkVariants = {
        square: data.url ?? null,
        tall: data.url_tall ?? null
    };
    state.currentAlbumName = data.album;

    updateMetadataUI({
        album: data.album,
        artist: data.artist,
        isCached
    });

    const preferredVariant = data.url ? 'square' : 'tall';
    await selectArtworkVariant(preferredVariant, true);
}

function setResolutionOptions(options, selectedUrl) {
    ui.resolutionSelect.innerHTML = '';

    options.forEach((option, index) => {
        const item = document.createElement('option');
        item.value = option.url;
        item.textContent = option.label;
        if (selectedUrl) {
            item.selected = option.url === selectedUrl;
        } else if (index === 0) {
            item.selected = true;
        }
        ui.resolutionSelect.appendChild(item);
    });

    if (!ui.resolutionSelect.value && options.length > 0) {
        ui.resolutionSelect.value = options[0].url;
    }
}

async function loadResolutionOptions(masterUrl) {
    const loadToken = ++state.resolutionLoadToken;
    ui.resolutionSelect.innerHTML = '<option value="">Loading...</option>';
    ui.resolutionSelect.disabled = true;

    try {
        const manifestText = await fetchText(masterUrl);
        const hasMasterVariants = manifestText.includes('#EXT-X-STREAM-INF');

        if (hasMasterVariants) {
            const variants = parseMasterVariants(manifestText, masterUrl);
            if (variants.length > 0) {
                if (loadToken !== state.resolutionLoadToken) {
                    return;
                }

                state.resolutionVariants = variants;
                setResolutionOptions(variants, variants[0].url);
                ui.resolutionSelect.disabled = false;
                return;
            }
        }

        if (loadToken !== state.resolutionLoadToken) {
            return;
        }

        state.resolutionVariants = [{ url: masterUrl, label: 'Source stream' }];
        setResolutionOptions(state.resolutionVariants, masterUrl);
        ui.resolutionSelect.disabled = true;
    } catch (error) {
        console.warn('Resolution parsing failed:', error);
        if (loadToken !== state.resolutionLoadToken) {
            return;
        }

        state.resolutionVariants = [{ url: masterUrl, label: 'Source stream' }];
        setResolutionOptions(state.resolutionVariants, masterUrl);
        ui.resolutionSelect.disabled = true;
    }
}

function parseMasterVariants(masterText, masterUrl) {
    const lines = masterText.split('\n').map((line) => line.trim()).filter(Boolean);
    const variants = [];

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        if (!line.startsWith('#EXT-X-STREAM-INF')) {
            continue;
        }

        const nextLine = lines[i + 1];
        if (!nextLine || nextLine.startsWith('#')) {
            continue;
        }

        const resolutionMatch = line.match(/RESOLUTION=(\d+x\d+)/i);
        const bandwidthMatch = line.match(/BANDWIDTH=(\d+)/i);
        const resolution = resolutionMatch ? resolutionMatch[1] : null;
        const bandwidth = bandwidthMatch ? Number(bandwidthMatch[1]) : 0;
        const url = new URL(nextLine, masterUrl).href;
        if (resolution) {
            const [w, h] = resolution.toLowerCase().split('x').map(Number);
            pixelCount = (w || 0) * (h || 0);
        }

        variants.push({
            url,
            resolution,
            bandwidth,
            pixelCount,
            label: resolution ? `${resolution}` : `Variant ${variants.length + 1}`
        });
    }

    const sortedVariants = variants.sort((a, b) => (b.pixelCount - a.pixelCount) || (b.bandwidth - a.bandwidth));

    const seenResolutions = new Set();

    return sortedVariants.filter((variant) => {
        if (seenResolutions.has(variant.resolution)) {
            return false;
        }
        
        seenResolutions.add(variant.resolution);
        return true;
    });
}

async function fetchText(url) {
    const response = await fetch(url);
    if (!response.ok) {
        throw new Error('Failed to load playlist.');
    }
    return response.text();
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
if (ui.variantSquareBtn) {
    ui.variantSquareBtn.addEventListener('click', () => {
        void selectArtworkVariant('square', true);
    });
}
if (ui.variantTallBtn) {
    ui.variantTallBtn.addEventListener('click', () => {
        void selectArtworkVariant('tall', true);
    });
}
    const statusPing = document.getElementById('statusPing');
    const statusDot = document.getElementById('statusDot');
    const statusText = document.getElementById('statusText');
    
    const statsContainer = document.getElementById('statsContainer');
    const statSearches = document.getElementById('statSearches');
    const statDownloads = document.getElementById('statDownloads');
    const statAnimated = document.getElementById('statAnimatedEntries');
    const statCache = document.getElementById('statCacheEntries');

    try {
        const res = await fetch('/api/v1/status');
        const data = await res.json();
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

        statSearches.textContent = data.totalSearches.toLocaleString() || '0';
        statDownloads.textContent = data.totalDownloads.toLocaleString() || '0';
        statAnimated.textContent = data.totalAnimatedEntries.toLocaleString() || '0';
        statCache.textContent = data.totalCacheEntries.toLocaleString() || '0';
        
        statsContainer.classList.remove('hidden');
        statsContainer.classList.add('flex');
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
            const previewUrl = item.url || item.url_tall || '';
            const li = document.createElement('li');
            li.className = 'glass-panel p-2 rounded-lg history-item flex items-center gap-3 transition-colors cursor-pointer hover:bg-white/5';
            li.innerHTML = `
                <div class="w-12 h-12 flex-shrink-0 rounded bg-gray-800 border border-gray-700 overflow-hidden relative shadow-inner">
                    <video id="hist-vid-${index}" data-index="${index}" data-url="${previewUrl}" class="w-full h-full object-cover" loop muted playsinline></video>
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

                void applyArtworkData(item, true);
                
                requestAnimationFrame(() => {
                    ui.videoContainer.scrollIntoView({
                        behavior: 'smooth',
                        block: 'center'
                    });
                });
            };

            ui.historyList.appendChild(li);
            
            const thumbnailVideo = document.getElementById(`hist-vid-${index}`);
            observer.observe(thumbnailVideo);
        });
    } catch (error) {
        console.error("Failed to fetch history:", error);
    }
}

async function ensureFfmpegLoaded() {
    if (ffmpeg) {
        return;
    }

    ffmpeg = new FFmpeg();
    ffmpeg.on('progress', ({ progress }) => {
        if (!activeProgressLabel) {
            return;
        }
        var percent = Math.min(Math.max(Math.round(progress * 100), 0), 100);
        activeProgressLabel.textContent = `Converting... ${percent}%`;
    });

    const baseUrl = window.location.origin + '/ffmpeg';
    await ffmpeg.load({
        coreURL: `${baseUrl}/ffmpeg-core.js`,
        wasmURL: `${baseUrl}/ffmpeg-core.wasm`
    });
}

function setDownloadButtonsBusy(isBusy) {
    const classList = ['opacity-50', 'cursor-not-allowed'];
    if (ui.downloadMp4Btn) ui.downloadMp4Btn.disabled = isBusy;
    if (ui.downloadWebpBtn) ui.downloadWebpBtn.disabled = isBusy;

    if (isBusy) {
        if (ui.downloadMp4Btn) ui.downloadMp4Btn.classList.add(...classList);
        if (ui.downloadWebpBtn) ui.downloadWebpBtn.classList.add(...classList);
    } else {
        if (ui.downloadMp4Btn) ui.downloadMp4Btn.classList.remove(...classList);
        if (ui.downloadWebpBtn) ui.downloadWebpBtn.classList.remove(...classList);
    }
}

function sanitizeFileName(input) {
    return (input || 'artwork')
        .replace(/[^a-z0-9]/gi, '_')
        .replace(/_+/g, '_')
        .replace(/^_+|_+$/g, '')
        .toLowerCase() || 'artwork';
}

async function safeDeleteFsFile(fileName) {
    try {
        await ffmpeg.deleteFile(fileName);
    } catch {
        // Ignore missing files during cleanup.
    }
}

async function resolveMediaPlaylistUrl(candidateUrl) {
    const manifestText = await fetchText(candidateUrl);
    if (!manifestText.includes('#EXT-X-STREAM-INF')) {
        return candidateUrl;
    }

    const variants = parseMasterVariants(manifestText, candidateUrl);
    if (variants.length === 0) {
        throw new Error('No playable stream variant found.');
    }

    return variants[0].url;
}

async function fetchSegmentUrls(mediaPlaylistUrl) {
    const text = await fetchText(mediaPlaylistUrl);
    const allSegments = text
        .split('\n')
        .map((line) => line.trim())
        .filter((line) => line.length > 0 && !line.startsWith('#'))
        .map((line) => new URL(line, mediaPlaylistUrl).href);

    return [...new Set(allSegments)];
}

async function reportDownloadStat(format) {
    const storageKey = `downloaded_${format}_${state.currentM3u8Url}`;
    if (localStorage.getItem(storageKey)) {
        return;
    }

    try {
        await fetch('/api/v1/artwork/download', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ m3u8Url: state.currentM3u8Url })
        });
        localStorage.setItem(storageKey, 'true');
    } catch (error) {
        console.warn('Failed to report download stat:', error);
    }
}

async function downloadArtwork(format) {
    if (!state.currentM3u8Url) return;

    const isWebp = format === 'webp';
    const buttonLabel = isWebp ? ui.downloadWebpBtnText : ui.downloadMp4BtnText;
    const defaultLabel = isWebp ? 'WebP' : 'MP4';
    const variantSuffix = state.selectedArtworkVariant === 'tall' ? 'tall' : 'square';
    const outputFileName = isWebp ? `output_${variantSuffix}.webp` : `output_${variantSuffix}.mp4`;

    try {
        setDownloadButtonsBusy(true);
        buttonLabel.textContent = 'Loading Engine...';
        activeProgressLabel = buttonLabel;
        await ensureFfmpegLoaded();

        const selectedStreamUrl = ui.resolutionSelect.value || state.currentM3u8Url;
        buttonLabel.textContent = 'Preparing stream...';
        const mediaPlaylistUrl = await resolveMediaPlaylistUrl(selectedStreamUrl);
        const segments = await fetchSegmentUrls(mediaPlaylistUrl);

        if (segments.length === 0) {
            throw new Error('No media segments found.');
        }

        let listFileContent = "";
        for (let i = 0; i < segments.length; i++) {
            buttonLabel.textContent = `Downloading ${i + 1}/${segments.length}...`;
            const segRes = await fetch(segments[i]);
            if (!segRes.ok) {
                throw new Error('Failed to download HLS segment.');
            }
            const segBuffer = await segRes.arrayBuffer();
            const fileName = `seg${i}.ts`;
            await ffmpeg.writeFile(fileName, new Uint8Array(segBuffer));
            listFileContent += `file '${fileName}'\n`;
        }

        await ffmpeg.writeFile('list.txt', listFileContent);

        buttonLabel.textContent = 'Merging video...';
        await ffmpeg.exec(['-f', 'concat', '-safe', '0', '-i', 'list.txt', '-c', 'copy', 'temp.mp4']);

        if (isWebp) {
            const quality = Number(ui.webpQuality.value) || 80;
            buttonLabel.textContent = 'Encoding WebP...';
            await ffmpeg.exec([
                '-i', 'temp.mp4',
                '-an',
                '-c:v', 'libwebp',
                '-loop', '0',
                '-q:v', String(quality),
                outputFileName
            ]);
        } else {
            await ffmpeg.exec(['-i', 'temp.mp4', '-c', 'copy', outputFileName]);
        }

        const data = await ffmpeg.readFile(outputFileName);
        const mimeType = isWebp ? 'image/webp' : 'video/mp4';
        const outputBlob = new Blob([data.buffer], { type: mimeType });
        const downloadUrl = URL.createObjectURL(outputBlob);

        const a = document.createElement('a');
        a.href = downloadUrl;
        const safeName = sanitizeFileName(state.currentAlbumName);
        a.download = `${safeName}_artwork_${variantSuffix}.${format}`;
        a.click();

        URL.revokeObjectURL(downloadUrl);
        await reportDownloadStat(format);

        buttonLabel.textContent = 'Done';
        setTimeout(() => {
            buttonLabel.textContent = defaultLabel;
        }, 2200);

        await safeDeleteFsFile(outputFileName);
        await safeDeleteFsFile('temp.mp4');
        await safeDeleteFsFile('list.txt');
        for (let i = 0; i < segments.length; i++) {
            await safeDeleteFsFile(`seg${i}.ts`);
        }

    } catch (error) {
        console.error('FFmpeg Error:', error);
        buttonLabel.textContent = 'Error - Retry';
        setTimeout(() => {
            buttonLabel.textContent = defaultLabel;
        }, 3000);
    } finally {
        activeProgressLabel = null;
        setDownloadButtonsBusy(false);
    }
}

ui.tabDetails.onclick = () => setMode('details');
ui.tabUrl.onclick = () => setMode('url');
if (ui.downloadMp4Btn) {
    ui.downloadMp4Btn.addEventListener('click', () => downloadArtwork('mp4'));
}
if (ui.downloadWebpBtn) {
    ui.downloadWebpBtn.addEventListener('click', () => downloadArtwork('webp'));
}
if (ui.variantSquareBtn) {
    ui.variantSquareBtn.addEventListener('click', () => {
        void selectArtworkVariant('square', true);
    });
}
if (ui.variantTallBtn) {
    ui.variantTallBtn.addEventListener('click', () => {
        void selectArtworkVariant('tall', true);
    });
}
if (ui.webpQuality && ui.webpQualityValue) {
    ui.webpQualityValue.textContent = ui.webpQuality.value;
    ui.webpQuality.addEventListener('input', () => {
        ui.webpQualityValue.textContent = ui.webpQuality.value;
    });
}

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
        
        await applyArtworkData(data, data.isCached);
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