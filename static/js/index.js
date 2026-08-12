window.HELP_IMPROVE_VIDEOJS = false;

$(document).ready(function() {
    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const environmentVideo = document.querySelector('.environment-video video');

    if (prefersReducedMotion && environmentVideo) {
      environmentVideo.removeAttribute('autoplay');
      environmentVideo.pause();
    }

    // Check for click events on the navbar burger icon
    $(".navbar-burger").click(function() {
      // Toggle the "is-active" class on both the "navbar-burger" and the "navbar-menu"
      $(".navbar-burger").toggleClass("is-active");
      $(".navbar-menu").toggleClass("is-active");
    });

    var options = {
        slidesToScroll: 1,
        slidesToShow: 3,
        loop: true,
        infinite: true,
        autoplay: !prefersReducedMotion,
        autoplaySpeed: 4000,
    }

    // Initialize all div with carousel class
    bulmaCarousel.attach('.carousel', options);
})
