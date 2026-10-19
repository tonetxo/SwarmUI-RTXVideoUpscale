// Register RTX Video action buttons in SwarmUI history context menus
(function() {
    // Purge any stale cookies or hidden DOM values that could hijack normal generations
    try {
        if (typeof deleteCookie === 'function') {
            deleteCookie('lastparam_input_rtxvideofile');
            deleteCookie('lastparam_input_rtxinterpolatevideofile');
        }
        let el1 = document.getElementById('input_rtxvideofile');
        if (el1) el1.value = '';
        let el2 = document.getElementById('input_rtxinterpolatevideofile');
        if (el2) el2.value = '';
    } catch (e) {
        console.error('Error cleaning RTX history param state:', e);
    }

    if (typeof registerMediaButton !== 'function') {
        console.warn('RTX Video: registerMediaButton is not available - SwarmUI version may be too old');
        return;
    }

    let videoExtensions = ['mp4', 'webm', 'gif', 'mov', 'avi', 'mkv'];

    function isVideoSrc(src) {
        let isDataImage = src.startsWith('data:');
        let isDataVideo = isDataImage && src.toLowerCase().startsWith('data:video/');
        if (isDataVideo) {
            return false; // Data URLs are not supported
        }
        if (typeof getMediaType === 'function' && getMediaType(src) === 'video') {
            return true;
        }
        let extension = src.split('.').pop().toLowerCase().split('?')[0];
        return videoExtensions.includes(extension);
    }

    // 1. RTX Upscale button
    registerMediaButton(
        'RTX Upscale',
        (src) => {
            if (typeof currentBackendFeatureSet === 'undefined' || !currentBackendFeatureSet.includes('rtx_video_upscale')) {
                let message = 'RTX Video Upscale is not available on the current backend. Please install or enable NVIDIA RTX Nodes.';
                console.warn('RTX Video: ' + message);
                if (typeof showToast === 'function') {
                    showToast(message);
                }
                return;
            }

            if (!isVideoSrc(src)) {
                return;
            }

            let input_overrides = { 'images': 1 };
            let fullsrc = typeof getImageFullSrc === 'function' ? getImageFullSrc(src) : src;
            let prefix = typeof getImageOutPrefix === 'function' ? getImageOutPrefix() : 'Output';
            input_overrides['rtxvideofile'] = prefix + '/' + fullsrc;

            let upscaleParams = [
                'rtxupscalequality',
                'rtxupscaleresizemode',
                'rtxupscalescale',
                'rtxupscaletargetwidth',
                'rtxupscaletargetheight',
                'rtxupscalestrength',
                'rtxupscaleencoding'
            ];

            for (let paramId of upscaleParams) {
                let elem = document.getElementById('input_' + paramId);
                if (elem) {
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
        ['video'],
        true,
        true
    );

    // 2. RTX Interpolate button
    registerMediaButton(
        'RTX Interpolate',
        (src) => {
            if (typeof currentBackendFeatureSet === 'undefined' || (!currentBackendFeatureSet.includes('rtx_video_frame_gen') && !currentBackendFeatureSet.includes('rtx_video_upscale'))) {
                let message = 'RTX Video Frame Generation is not available on the current backend. Please install or enable NVIDIA RTX Nodes.';
                console.warn('RTX Video: ' + message);
                if (typeof showToast === 'function') {
                    showToast(message);
                }
                return;
            }

            if (!isVideoSrc(src)) {
                return;
            }

            let input_overrides = { 'images': 1 };
            let fullsrc = typeof getImageFullSrc === 'function' ? getImageFullSrc(src) : src;
            let prefix = typeof getImageOutPrefix === 'function' ? getImageOutPrefix() : 'Output';
            input_overrides['rtxinterpolatevideofile'] = prefix + '/' + fullsrc;

            let interpParams = [
                'rtxframeinterpolation',
                'rtxframegenadjustfps',
                'rtxframegenmode',
                'rtxframegenshotdetection',
                'rtxframegenencoding'
            ];

            for (let paramId of interpParams) {
                let elem = document.getElementById('input_' + paramId);
                if (elem) {
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

            // Ensure multiplier is at least 2 when clicking the interpolation button directly
            if (!input_overrides['rtxframeinterpolation'] || parseInt(input_overrides['rtxframeinterpolation']) < 2) {
                input_overrides['rtxframeinterpolation'] = 2;
            }

            if (typeof mainGenHandler !== 'undefined' && mainGenHandler.doGenerate) {
                mainGenHandler.doGenerate(input_overrides, {});
            }
        },
        'Interpolate frames of this video using NVIDIA RTX Video Frame Generation',
        ['video'],
        true,
        true
    );
})();
