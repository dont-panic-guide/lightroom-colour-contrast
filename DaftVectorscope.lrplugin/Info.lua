return {
    LrSdkVersion = 11.0,
    LrToolkitIdentifier = 'com.daft.lightroom.vectorscope',
    LrPluginName = 'Daft Vectorscope',
    LrPluginInfoUrl = 'https://github.com/daft/lightroom-vectorscope',

    -- File > Plug-in Extras (accessible from all modules including Develop)
    LrExportMenuItems = {
        {
            title = "Open Vectorscope...",
            file = "ShowVectorscope.lua",
        },
    },

    -- Library > Plug-in Extras
    LrLibraryMenuItems = {
        {
            title = "Open Vectorscope...",
            file = "ShowVectorscope.lua",
        },
    },

    LrHelpMenuItems = {
        {
            title = "Vectorscope User Guide",
            file = "Help.lua",
        },
    },

    VERSION = { major = 1, minor = 0, revision = 0, build = 1 },
}
