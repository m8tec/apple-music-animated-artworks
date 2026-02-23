// --- DOM Elemente ---
const form = document.getElementById('searchForm');
const submitBtn = document.getElementById('submitBtn');
const spinner = document.getElementById('loadingSpinner');
const statusMessage = document.getElementById('statusMessage');
const videoContainer = document.getElementById('videoContainer');
const videoElement = document.getElementById('artworkVideo');
const rawLink = document.getElementById('rawLink');
const historyContainer = document.getElementById('historyContainer');
const historyList = document.getElementById('historyList');

let mainHls; // Globale HLS Instanz für den Haupt-Player
let historyHlsInstances = []; // Array für das Memory-Management der Historien-Videos

// --- Video Player Funktion (Hauptplayer) ---
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

// --- Historie vom Server laden ---
async function fetchGlobalHistory() {
    try {
        const response = await fetch('/api/v1/artwork/history');
        if (!response.ok) return;

        const historyData = await response.json();
        
        if (historyData.length > 0) {
            historyContainer.classList.remove('hidden');
            historyList.innerHTML = ''; // Liste im DOM leeren
            
            // WICHTIG: Memory-Leak verhindern! Alle alten HLS Instanzen der Historie killen.
            historyHlsInstances.forEach(hls => hls.destroy());
            historyHlsInstances = []; 
            
            historyData.forEach((item, index) => {
                const li = document.createElement('li');
                li.className = 'glass-panel p-2 rounded-lg history-item flex items-center gap-3 transition-colors';
                
                // Wir fügen ein kleines Video-Element als Thumbnail ein
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
                
                // Klick auf Eintrag -> direkt im großen Player abspielen
                li.onclick = () => {
                    document.getElementById('artistInput').value = item.artist;
                    document.getElementById('albumInput').value = item.album;
                    playVideo(item.url);
                    window.scrollTo({ top: 0, behavior: 'smooth' }); // Scrollt sanft nach oben
                };
                
                historyList.appendChild(li);

                // --- Thumbnail Video Playback Logik ---
                const thumbnailVideo = document.getElementById(`hist-vid-${index}`);
                
                if (Hls.isSupported()) {
                    // Wir konfigurieren hls.js hier etwas leichtgewichtiger, da es nur Thumbnails sind
                    const thumbHls = new Hls({
                        capLevelToPlayerSize: true, // Lädt nicht die 4K-Version für ein 48px Bild!
                        autoStartLoad: true
                    });
                    thumbHls.loadSource(item.url);
                    thumbHls.attachMedia(thumbnailVideo);
                    thumbHls.on(Hls.Events.MANIFEST_PARSED, () => {
                        thumbnailVideo.play().catch(e => console.log("Thumb Autoplay prevented:", e));
                    });
                    historyHlsInstances.push(thumbHls); // Für späteres Cleanup speichern
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

// --- Formular absenden ---
form.addEventListener('submit', async (e) => {
    e.preventDefault();
    
    const artist = document.getElementById('artistInput').value.trim();
    const album = document.getElementById('albumInput').value.trim();

    statusMessage.classList.add('hidden');
    videoContainer.classList.add('hidden');
    rawLink.classList.add('hidden');
    submitBtn.disabled = true;
    spinner.classList.remove('hidden');

    try {
        const params = new URLSearchParams({ artist, album });
        const response = await fetch(`/api/v1/artwork?${params.toString()}`);
        
        if (!response.ok) {
            if (response.status >= 500) {
                throw new Error('Server Error (500): Etwas ist auf dem Server schiefgelaufen.');
            } else if (response.status === 404) {
                throw new Error('No animated artwork found for this album.');
            } else {
                throw new Error(`Es ist ein unerwarteter Fehler aufgetreten (Status: ${response.status}).`);
            }
        }

        const data = await response.json();
        playVideo(data.url);
        fetchGlobalHistory();

    } catch (error) {
        statusMessage.textContent = error.message;
        statusMessage.className = "mt-4 text-center text-sm text-red-400";
        statusMessage.classList.remove('hidden');
    } finally {
        submitBtn.disabled = false;
        spinner.classList.add('hidden');
    }
});

// Beim Start aufrufen
fetchGlobalHistory();