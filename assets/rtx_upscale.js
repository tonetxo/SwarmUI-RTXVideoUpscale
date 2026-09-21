// Add RTX Upscale button to video context menus via SwarmUI's registerMediaButton API.
(function() {
    if (typeof registerMediaButton !== 'function') {
        console.warn('RTX Upscale: registerMediaButton is not available - SwarmUI version may be too old');
        return;
    }

    let videoExtensions = ['mp4', 'webm', 'gif', 'mov', 'avi', 'mkv'];

    registerMediaButton(
        'RTX Upscale',
        (src) => {
            if (typeof currentBackendFeatureSet === 'undefined' || !currentBackendFeatureSet.includes('rtx_video_upscale')) {
                let message = 'RTX Upscale is not available on the current backend. Please install or enable the NVIDIA RTX Nodes node pack first.';
                console.warn('RTX Upscale: ' + message);
                if (typeof showToast === 'function') {
                    showToast(message);
                }
                return;
            }

            let isDataImage = src.startsWith('data:');
            let isDataVideo = isDataImage && src.toLowerCase().startsWith('data:video/');
            let isVideo = isDataVideo;
            if (!isDataImage) {
                let extension = src.split('.').pop().toLowerCase().split('?')[0];
                isVideo = videoExtensions.includes(extension);
            }

            // Data URL videos are not supported.
            if (isDataVideo) {
                return;
            }

            // Only videos are supported.
            if (!isVideo) {
                return;
            }

            let input_overrides = { 'images': 1 };

            let fullsrc = typeof getImageFullSrc === 'function' ? getImageFullSrc(src) : src;
            let prefix = typeof getImageOutPrefix === 'function' ? getImageOutPrefix() : 'Output';
            input_overrides['rtxvideofile'] = prefix + '/' + fullsrc;

            // Read RTX params from UI.
            let rtxParams = ['rtxupscalequality', 'rtxupscalescale'];
            for (let paramId of rtxParams) {
                let elem = document.getElementById('input_' + paramId);
                if (elem) {
                    // Skip toggleable params whose toggle is off (same logic as simpletab.js).
                    let toggleElem = document.getElementById('input_' + paramId + '_toggle');
                    if (toggleElem && !toggleElem.checked) {
                        continue;
                    }
                    let val = typeof getInputVal === 'function' ? getInputVal(elem, true) : elem.value;
                    if (val != null && val !== '') {
                        input_overrides[paramId] = val;
                    }
                }
            }

            if (typeof mainGenHandler !== 'undefined' && mainGenHandler.doGenerate) {
                mainGenHandler.doGenerate(input_overrides, {});
            }
        },
        'Upscale this video using NVIDIA RTX Video Super Resolution',
        ['video'],  // mediaTypes: 'video' (SwarmUI expects media category, not file extensions)
        true,       // isDefault: true promotes button to be visible directly on the toolbar
        true        // showInHistory
    );
})();
