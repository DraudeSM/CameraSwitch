obs = obslua

function script_load(settings)
    obs.timer_add(start_vcam, 1000)
end

function start_vcam()
    obs.timer_remove(start_vcam)
    if not obs.obs_frontend_virtualcam_active() then
        obs.obs_frontend_start_virtualcam()
    end
end

function script_description()
    return "Inicia automáticamente la Cámara Virtual al cargar OBS."
end